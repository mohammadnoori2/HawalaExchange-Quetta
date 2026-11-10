using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Security;
using HawalaExchange.Web.Components.Pages.HawalaTransactions;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;
public sealed class CashExecutedHawalaUiTests
{
    private const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    [Fact]
    public async Task Pagination_preserves_applied_filters_and_reset_uses_business_today()
    {
        var service=new Mock<ICashExecutedHawalaService>();service.Setup(x=>x.GetAsync(It.IsAny<CashExecutedHawalaFilter>(),It.IsAny<int>(),It.IsAny<int>(),It.IsAny<CancellationToken>())).ReturnsAsync(new CashExecutedHawalaResult());
        var page=new CashExecutedHawalas();var type=typeof(CashExecutedHawalas);
        type.GetProperty("Service",Hidden)!.SetValue(page,service.Object);
        var today=new DateTime(2052,3,15);type.GetField("lookups",Hidden)!.SetValue(page,new CashExecutedHawalaLookups{Today=today});type.GetMethod("Reset",Hidden)!.Invoke(page,null);
        var filter=(CashExecutedHawalaFilter)type.GetField("filter",Hidden)!.GetValue(page)!;Assert.Equal(today,filter.From);Assert.Equal(today,filter.To);Assert.Equal("Paid",filter.State);
        filter.AccountId=42;await (Task)type.GetMethod("Load",Hidden)!.Invoke(page,[1,true])!;
        filter.AccountId=99;await (Task)type.GetMethod("Load",Hidden)!.Invoke(page,[2,false])!;
        service.Verify(x=>x.GetAsync(It.Is<CashExecutedHawalaFilter>(f=>f.AccountId==42),2,50,It.IsAny<CancellationToken>()),Times.Once);
        await page.DisposeAsync();
    }
    [Theory]
    [InlineData("/hawala-cash-executed")]
    [InlineData("/hawala-cash-executed?x=1")]
    public void Route_is_guarded(string route)=>Assert.Equal("HawalaExecution.View",PermissionCatalog.PagePermission(route));
}
