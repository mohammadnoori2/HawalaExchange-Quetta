using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Security;
using HawalaExchange.Web.Components.Pages.Correspondents;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;
public sealed class AgentCommissionReportUiTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    [Fact]
    public void Opening_and_resetting_the_report_default_to_business_today_and_commission_date()
    {
        var page = new AgentCommissionReport { CorrespondentId = 42 };
        var today = new DateTime(2050, 5, 12);
        typeof(AgentCommissionReport).GetField("lookups", Hidden)!.SetValue(page, new AgentCommissionReportLookups { Today = today });
        typeof(AgentCommissionReport).GetMethod("ResetFilters", Hidden)!.Invoke(page, null);
        var filter = (AgentCommissionReportFilter)typeof(AgentCommissionReport).GetField("filter", Hidden)!.GetValue(page)!;
        Assert.Equal(today, filter.From); Assert.Equal(today, filter.To); Assert.Equal("Commission", filter.DateBasis);
    }
    [Fact]
    public async Task Detail_queries_use_the_applied_report_filters_after_the_editable_filters_change()
    {
        var service = new Mock<IAgentCommissionReportService>();
        service.Setup(x => x.GetAsync(It.IsAny<AgentCommissionReportFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AgentCommissionReportResult());
        service.Setup(x => x.GetDetailsAsync(It.IsAny<AgentCommissionReportFilter>(), It.IsAny<long?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResult<AgentCommissionReportRow>());
        var page = new AgentCommissionReport { CorrespondentId = 42 };
        typeof(AgentCommissionReport).GetProperty("Service", Hidden)!.SetValue(page, service.Object);
        Assert.Equal(0, typeof(AgentCommissionReport).GetProperty("FilterCount", Hidden)!.GetValue(page));
        var filter = (AgentCommissionReportFilter)typeof(AgentCommissionReport).GetField("filter", Hidden)!.GetValue(page)!;
        filter.From = new(2049, 1, 1); filter.To = new(2049, 1, 31); filter.Settlement = "Historical";
        await (Task)typeof(AgentCommissionReport).GetMethod("LoadReport", Hidden)!.Invoke(page, null)!;
        filter.From = new(2050, 1, 1); filter.Settlement = "Paid";
        await (Task)typeof(AgentCommissionReport).GetMethod("OpenLocation", Hidden)!.Invoke(page, [new AgentCommissionLocationSummary { LocationId = 12, Location = "Test" }])!;
        service.Verify(x => x.GetDetailsAsync(It.Is<AgentCommissionReportFilter>(f => f.CorrespondentId == 42 && f.From == new DateTime(2049, 1, 1) && f.Settlement == "Historical"), 12, 1, 50, It.IsAny<CancellationToken>()), Times.Once);
        await page.DisposeAsync();
    }
    [Theory]
    [InlineData("/correspondents/42/agent-commission-report")]
    [InlineData("/correspondents/42/agent-commission-report?x=1")]
    public void Direct_report_routes_require_correspondent_report_view_permission(string route) => Assert.Equal("CorrespondentReports.View", PermissionCatalog.PagePermission(route));
}
