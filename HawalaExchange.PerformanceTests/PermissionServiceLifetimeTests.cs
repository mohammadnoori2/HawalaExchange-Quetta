using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

public class PermissionServiceLifetimeTests
{
    [Fact]
    public async Task Disposed_permission_service_cancels_without_touching_its_context_factory()
    {
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>(MockBehavior.Strict);
        using var permissions = Create(factory);
        permissions.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => permissions.RefreshAsync(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => permissions.RequireAsync("Customers.View"));
        factory.Verify(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(permissions.CurrentPermissions); Assert.False(permissions.Can("Customers.View"));
    }

    [Fact]
    public async Task Disposal_cancels_queued_refresh_and_stops_inflight_refresh_before_accessing_a_disposed_provider()
    {
        var completion = new TaskCompletionSource<ApplicationDbContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).Returns(completion.Task);
        using var permissions = Create(factory);
        var changes = 0; permissions.Changed += () => changes++;
        var running = permissions.RefreshAsync(true);
        var queued = permissions.RefreshAsync(true);
        using var services = new ServiceCollection().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=UnusedLifetimeTest;Integrated Security=true")
            .UseApplicationServiceProvider(services).Options;
        var context = new ApplicationDbContext(options, new TestCurrentTenant { UserId = 1 });
        services.Dispose(); permissions.Dispose();
        completion.SetResult(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        factory.Verify(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, changes); Assert.Empty(permissions.CurrentPermissions);
    }

    [Fact]
    public async Task Provider_disposal_during_scope_shutdown_is_reported_as_cancellation()
    {
        var completion = new TaskCompletionSource<ApplicationDbContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).Returns(completion.Task);
        using var permissions = Create(factory);
        var running = permissions.RefreshAsync(true);
        permissions.Dispose();
        completion.SetException(new ObjectDisposedException("IServiceProvider"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.False(permissions.Can("Customers.View"));
    }

    [Fact]
    public async Task Unexpected_disposal_while_permission_service_is_active_is_not_hidden()
    {
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ObjectDisposedException("Unexpected provider disposal"));
        using var permissions = Create(factory);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => permissions.RefreshAsync(true));
    }

    private static UserPermissionService Create(Mock<IDbContextFactory<ApplicationDbContext>> factory)
        => new(factory.Object, new TestCurrentTenant { UserId = 1 }, new UserPermissionChangeBus(), NullLogger<UserPermissionService>.Instance);
}
