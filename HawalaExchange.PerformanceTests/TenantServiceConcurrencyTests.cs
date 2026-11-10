using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HawalaExchange.PerformanceTests;

public class TenantServiceConcurrencyTests
{
    [Fact]
    public async Task Task_only_mutation_and_read_share_the_same_gate()
    {
        using var provider = Services();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var customer = new Mock<ICustomerService>();
        customer.Setup(x => x.DeleteAsync(1)).Returns(async () => { entered.SetResult(); await release.Task; });
        var first = Wrap(customer.Object, provider).DeleteAsync(1); await entered.Task;
        var currency = new Mock<ICurrencyService>(); currency.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CurrencyDto>());
        var second = Wrap(currency.Object, provider).GetAllAsync();
        try { Assert.False(second.IsCompleted); }
        finally { release.TrySetResult(); await first; }
        Assert.Empty(await second.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Different_services_sharing_one_scope_do_not_execute_concurrently()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currency = new Mock<ICurrencyService>();
        currency.Setup(x => x.GetAllAsync()).Returns(async () => { entered.SetResult(); await release.Task; return Array.Empty<CurrencyDto>(); });
        var branches = new Mock<IBranchService>(); branches.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<BranchDto>());
        using var provider = Services();
        var first = Wrap(currency.Object, provider).GetAllAsync(); await entered.Task;
        var second = Wrap(branches.Object, provider).GetAllAsync();
        try { Assert.False(second.IsCompleted); branches.Verify(x => x.GetAllAsync(), Times.Never); }
        finally { release.TrySetResult(); await first; }
        Assert.Empty(await second.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Separate_scopes_are_independent_and_nested_service_calls_do_not_deadlock()
    {
        using var root = Services(); using var firstScope = root.CreateScope(); using var secondScope = root.CreateScope();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currency = new Mock<ICurrencyService>();
        currency.Setup(x => x.GetAllAsync()).Returns(async () => { entered.SetResult(); await release.Task; return Array.Empty<CurrencyDto>(); });
        var first = Wrap(currency.Object, firstScope.ServiceProvider).GetAllAsync(); await entered.Task;
        var branches = new Mock<IBranchService>(); branches.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<BranchDto>());
        try { Assert.Empty(await Wrap(branches.Object, secondScope.ServiceProvider).GetAllAsync().WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { release.TrySetResult(); await first; }
        var nested = Wrap(branches.Object, firstScope.ServiceProvider);
        currency.Setup(x => x.GetAllAsync()).Returns(async () => { await nested.GetAllAsync(); return Array.Empty<CurrencyDto>(); });
        Assert.Empty(await Wrap(currency.Object, firstScope.ServiceProvider).GetAllAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Cancelled_waiter_never_runs_its_service_and_a_failed_call_releases_the_scope()
    {
        using var provider = Services();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currency = new Mock<ICurrencyService>();
        currency.Setup(x => x.GetAllAsync()).Returns(async () => { entered.SetResult(); await release.Task; throw new InvalidOperationException("Controlled query failure"); });
        var first = Wrap(currency.Object, provider).GetAllAsync(); await entered.Task;
        var aed = new Mock<IAedDealService>(); aed.Setup(x => x.GetAllAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AedDealDto>());
        using var cancel = new CancellationTokenSource();
        var queued = Wrap(aed.Object, provider).GetAllAsync(null, cancel.Token); cancel.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued); aed.Verify(x => x.GetAllAsync(null, It.IsAny<CancellationToken>()), Times.Never); }
        finally { release.TrySetResult(); await Assert.ThrowsAsync<InvalidOperationException>(() => first); }
        Assert.Empty(await Wrap(aed.Object, provider).GetAllAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static T Wrap<T>(T target, IServiceProvider services) where T : class
        => (T)PermissionServiceProxy.Wrap(typeof(T), target, services);
    private static ServiceProvider Services()
    {
        var permission = new Mock<IUserPermissionService>();
        permission.SetupGet(x => x.CurrentPermissions).Returns(new HashSet<string> { "Currencies.View", "Branches.View", "AedDeals.View" });
        permission.Setup(x => x.RefreshAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        permission.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new ServiceCollection().AddSingleton(permission.Object).AddSingleton(Mock.Of<IDbContextFactory<ApplicationDbContext>>()).BuildServiceProvider();
    }
}
