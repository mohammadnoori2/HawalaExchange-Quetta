using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public class AgentBillExecutionUiTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(AgentBillActions component, string field) => (T)typeof(AgentBillActions).GetField(field, Hidden)!.GetValue(component)!;
    private static Task Invoke(AgentBillActions component, string method) => (Task)typeof(AgentBillActions).GetMethod(method, Hidden)!.Invoke(component, null)!;

    [Fact]
    public async Task Historical_mode_never_loads_payment_accounts_or_calls_the_payment_operations()
    {
        var service = new Mock<IHawalaService>();
        service.Setup(x => x.GetAgentBillsForHawalasAsync(42, It.IsAny<IReadOnlyCollection<long>>())).ReturnsAsync(new List<AgentBillDto>());
        service.Setup(x => x.PreviewAgentBillAsync(It.IsAny<AgentBillCreateDto>())).ReturnsAsync(new AgentBillDto { IsHistoricalReceipt = true, ConfirmationToken = "history-snapshot" });
        AgentBillHistoricalDto? captured = null;
        service.Setup(x => x.CreateHistoricalAgentReceiptAsync(It.IsAny<AgentBillHistoricalDto>())).Callback<AgentBillHistoricalDto>(x => captured = x)
            .ThrowsAsync(new TimeoutException("Retry the same receipt"));
        var component = new AgentBillActions { CorrespondentId = 42, SelectedIds = new long[] { 13 } };
        typeof(AgentBillActions).GetProperty("Service", Hidden)!.SetValue(component, service.Object);
        await Invoke(component, "OpenHistorical");
        var bill = Read<AgentBillCreateDto>(component, "request");
        Assert.True(bill.HistoricalReceipt); Assert.False(bill.ConfirmPreviouslySettled);
        bill.RecipientName = "Old agent"; bill.PreviousSettlementReference = "Receipt 123"; bill.ConfirmPreviouslySettled = true;
        await Invoke(component, "Preview"); await Invoke(component, "Create");
        Assert.NotNull(captured); Assert.Equal("history-snapshot", captured.Bill.PreviewToken);
        service.Verify(x => x.GetAgentBillPaymentAccountsAsync(), Times.Never);
        service.Verify(x => x.ExecuteAndCreateAgentBillAsync(It.IsAny<AgentBillExecuteDto>()), Times.Never);
        service.Verify(x => x.CreateAgentBillAsync(It.IsAny<AgentBillCreateDto>()), Times.Never);
        service.Verify(x => x.PayAgentBillAsync(It.IsAny<AgentBillPayDto>()), Times.Never);
    }

    [Fact]
    public async Task Cash_defaults_and_retries_keep_the_same_atomic_request_and_all_currencies()
    {
        var service = new Mock<IHawalaService>();
        service.Setup(x => x.GetAgentBillPaymentAccountsAsync()).ReturnsAsync(new List<AccountDto> {
            new() { Id = 1, AccountCode = "1101", AccountType = "Bank", AccountName = "Bank" },
            new() { Id = 8, AccountCode = "1001", AccountType = "Cash", AccountName = "Cash" } });
        service.Setup(x => x.GetAgentBillsForHawalasAsync(42, It.IsAny<IReadOnlyCollection<long>>())).ReturnsAsync(new List<AgentBillDto>());
        service.Setup(x => x.PreviewAgentBillAsync(It.IsAny<AgentBillCreateDto>())).ReturnsAsync(new AgentBillDto {
            ConfirmationToken = "snapshot", Totals = [new() { CurrencyId = 1, Principal = 100 }, new() { CurrencyId = 2, AgentCommission = 8 }] });
        var requests = new List<AgentBillExecuteDto>();
        service.Setup(x => x.ExecuteAndCreateAgentBillAsync(It.IsAny<AgentBillExecuteDto>())).Callback<AgentBillExecuteDto>(requests.Add)
            .ThrowsAsync(new TimeoutException("Retry the same request"));
        var component = new AgentBillActions { CorrespondentId = 42, SelectedIds = new long[] { 13, 14 } };
        typeof(AgentBillActions).GetProperty("Service", Hidden)!.SetValue(component, service.Object);
        await Invoke(component, "Open");
        Assert.Equal(8, Read<long>(component, "paymentAccountId"));
        var bill = Read<AgentBillCreateDto>(component, "request"); bill.RecipientName = "Agent"; bill.ConfirmUnsettled = true;
        await Invoke(component, "Preview"); await Invoke(component, "Create"); await Invoke(component, "Create");
        Assert.Equal(2, requests.Count); Assert.Equal(requests[0].RequestId, requests[1].RequestId);
        Assert.NotEqual(Guid.Empty, requests[0].RequestId); Assert.Equal("snapshot", requests[0].Bill.PreviewToken);
        Assert.Equal(new long[] { 1, 2 }, requests[0].Accounts.Select(x => x.CurrencyId));
        Assert.All(requests[0].Accounts, x => Assert.Equal(8, x.AccountId));
        service.Verify(x => x.ConfirmAgentExecutionAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<long>>()), Times.Never);
        service.Verify(x => x.CreateAgentBillAsync(It.IsAny<AgentBillCreateDto>()), Times.Never);
        service.Verify(x => x.PayAgentBillAsync(It.IsAny<AgentBillPayDto>()), Times.Never);
    }
}
