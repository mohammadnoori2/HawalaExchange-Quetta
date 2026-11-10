using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Web.Components.Pages.Correspondents;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public sealed class AgentCommissionEditTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(CorrespondentHawalaPanel panel, string name) => (T)typeof(CorrespondentHawalaPanel).GetField(name, Hidden)!.GetValue(panel)!;
    private static void Set(CorrespondentHawalaPanel panel, string name, object value) => typeof(CorrespondentHawalaPanel).GetField(name, Hidden)!.SetValue(panel, value);
    private static Task Invoke(CorrespondentHawalaPanel panel, string method, params object[] args) =>
        (Task)typeof(CorrespondentHawalaPanel).GetMethod(method, Hidden)!.Invoke(panel, args)!;

    [Fact]
    public async Task Editing_prefills_existing_amount_and_currency_and_saves_the_outgoing_id()
    {
        var (panel, service) = Create();
        var sent = new HawalaDto { Id = 13, SourceHawalaId = 99, IsSystemGenerated = true,
            AgentCommissionAmount = 12, AgentCommissionCurrencyId = 2, ToCurrencyId = 1 };
        await Invoke(panel, "OpenAddCommissionAsync", sent);
        Assert.Equal(12, Read<decimal?>(panel, "commissionAmount"));
        Assert.Equal(2, Read<long>(panel, "commissionCurrencyId"));
        Set(panel, "commissionAmount", (decimal?)6m);
        await Invoke(panel, "SaveAgentCommissionAsync");
        service.Verify(x => x.UpdateAgentCommissionAsync(13, It.Is<UpdateHawalaAgentCommissionDto>(d =>
            d.Amount == 6 && d.CurrencyId == 2 && d.PreviousAmount == 12 && d.PreviousCurrencyId == 2)), Times.Once);
        service.Verify(x => x.AddAgentCommissionAsync(It.IsAny<long>(), It.IsAny<AddHawalaAgentCommissionDto>()), Times.Never);
        service.Verify(x => x.GetHawalaByIdAsync(It.IsAny<long>()), Times.Never);
        Assert.False(Read<bool>(panel, "showCommissionModal"));
        service.Verify(x => x.GetHawalasAsync(It.IsAny<HawalaFilterDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failed_edit_keeps_entered_amount_and_shows_the_reason()
    {
        var (panel, service) = Create();
        service.Setup(x => x.UpdateAgentCommissionAsync(It.IsAny<long>(), It.IsAny<UpdateHawalaAgentCommissionDto>()))
            .ThrowsAsync(new InvalidOperationException("بیل فعال است"));
        await Invoke(panel, "OpenAddCommissionAsync", new HawalaDto { Id = 13, AgentCommissionAmount = 12, AgentCommissionCurrencyId = 2 });
        Set(panel, "commissionAmount", (decimal?)6m);
        await Invoke(panel, "SaveAgentCommissionAsync");
        Assert.True(Read<bool>(panel, "showCommissionModal"));
        Assert.Equal(6, Read<decimal?>(panel, "commissionAmount"));
        Assert.Equal("بیل فعال است", Read<string>(panel, "commissionError"));
        Assert.False(Read<bool>(panel, "actionLoading"));
    }

    [Fact]
    public async Task Adding_missing_commission_still_uses_the_add_operation()
    {
        var (panel, service) = Create();
        await Invoke(panel, "OpenAddCommissionAsync", new HawalaDto { Id = 13, ToCurrencyId = 2 });
        Assert.Null(Read<decimal?>(panel, "commissionAmount"));
        Set(panel, "commissionAmount", (decimal?)6m);
        await Invoke(panel, "SaveAgentCommissionAsync");
        service.Verify(x => x.AddAgentCommissionAsync(13, It.Is<AddHawalaAgentCommissionDto>(d => d.Amount == 6 && d.CurrencyId == 2)), Times.Once);
        service.Verify(x => x.UpdateAgentCommissionAsync(It.IsAny<long>(), It.IsAny<UpdateHawalaAgentCommissionDto>()), Times.Never);
    }

    private static (CorrespondentHawalaPanel, Mock<IHawalaService>) Create()
    {
        var panel = new CorrespondentHawalaPanel();
        var service = new Mock<IHawalaService>();
        service.Setup(x => x.GetHawalasAsync(It.IsAny<HawalaFilterDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(new HawalaListResultDto());
        typeof(CorrespondentHawalaPanel).GetProperty("HawalaService", Hidden)!.SetValue(panel, service.Object);
        Set(panel, "currenciesLoaded", true);
        return (panel, service);
    }
}
