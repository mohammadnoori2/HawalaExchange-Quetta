using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Web.Components.Pages.Correspondents;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public sealed class CorrespondentStatusReloadTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(CorrespondentStatus page, string name) => (T)typeof(CorrespondentStatus).GetField(name, Hidden)!.GetValue(page)!;
    private static Task Invoke(CorrespondentStatus page, string method, params object[] arguments) =>
        (Task)typeof(CorrespondentStatus).GetMethod(method, Hidden)!.Invoke(page, arguments)!;
    private static (CorrespondentStatus, Mock<ICorrespondentService>) Create()
    {
        var service = new Mock<ICorrespondentService>();
        var page = new CorrespondentStatus { CorrespondentId = 42 };
        typeof(CorrespondentStatus).GetProperty("CorrespondentService", Hidden)!.SetValue(page, service.Object);
        return (page, service);
    }
    private static CorrespondentStatusPageDto Data(decimal balance) => new()
    {
        Correspondent = new() { Id = 42, Name = "Test correspondent", Code = "42" }, AccountId = 8,
        Balances = [new() { CurrencyId = 2, CurrencyCode = "USD", Balance = balance }],
        Operations = new() { PageNumber = 1, PageSize = 10 }
    };

    [Fact]
    public async Task Timeout_keeps_previous_balances_and_retry_loads_one_fresh_snapshot()
    {
        var (page, service) = Create();
        service.SetupSequence(x => x.GetStatusPageAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Data(100)).ThrowsAsync(new TimeoutException()).ReturnsAsync(Data(150));
        await Invoke(page, "LoadCorrespondentAsync", CancellationToken.None);
        var oldAccount = Read<AccountDto>(page, "accountForView");
        var version = Read<int>(page, "dataVersion");
        await Invoke(page, "RefreshPageDataAsync");
        Assert.Same(oldAccount, Read<AccountDto>(page, "accountForView"));
        Assert.Equal(100, Assert.Single(Read<IReadOnlyList<BalanceDto>>(page, "initialBalances")).Balance);
        Assert.Equal(version, Read<int>(page, "dataVersion"));
        Assert.Contains("بیش از حد", Read<string>(page, "pageError"));
        await Invoke(page, "RefreshPageDataAsync");
        Assert.Equal(150, Assert.Single(Read<IReadOnlyList<BalanceDto>>(page, "initialBalances")).Balance);
        Assert.Equal(version + 1, Read<int>(page, "dataVersion"));
        Assert.Empty(Read<string>(page, "pageError"));
        service.Verify(x => x.GetStatusPageAsync(42, It.IsAny<CancellationToken>()), Times.Exactly(3));
        page.Dispose();
    }

    [Fact]
    public async Task Repeated_refresh_clicks_do_not_start_concurrent_database_requests()
    {
        var (page, service) = Create();
        service.Setup(x => x.GetStatusPageAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(Data(100));
        await Invoke(page, "LoadCorrespondentAsync", CancellationToken.None);
        var pending = new TaskCompletionSource<CorrespondentStatusPageDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(x => x.GetStatusPageAsync(42, It.IsAny<CancellationToken>())).Returns(pending.Task);
        var refresh = Invoke(page, "RefreshPageDataAsync");
        await Invoke(page, "RefreshPageDataAsync");
        service.Verify(x => x.GetStatusPageAsync(42, It.IsAny<CancellationToken>()), Times.Exactly(2));
        pending.SetResult(Data(200)); await refresh;
        Assert.Equal(200, Assert.Single(Read<IReadOnlyList<BalanceDto>>(page, "initialBalances")).Balance);
        page.Dispose();
    }
}
