using System.Net;
using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared.Modals;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;

namespace HawalaExchange.PerformanceTests;

public sealed class SettlementModalTests
{
    [Fact]
    public async Task All_days_register_separately_and_retry_only_failed_day_with_its_own_rate()
    {
        var day=DateTime.Today.AddDays(-2);var next=day.AddDays(1);var calls=new List<ConvertHawalasToSettlementDto>();
        var service=new Mock<ICorrespondentSettlementService>();
        service.Setup(x=>x.GetPreviewAsync(1,It.IsAny<CancellationToken>())).ReturnsAsync(new CorrespondentSettlementPreviewDto{CorrespondentId=1,TargetCurrencyId=2,TargetCurrencyCode="USD",TargetQuotationPriority=10,TargetDecimalPlaces=0});
        service.Setup(x=>x.GetHawalaPreviewAsync(1,It.IsAny<IReadOnlyCollection<long>>(),It.IsAny<CancellationToken>())).ReturnsAsync(new[]{new CorrespondentSettlementHawalaBalanceDto{HawalaId=1,HawalaNumber=101,RateDate=day,SourceCurrencyId=1,SourceCurrencyCode="AFN",SourceQuotationPriority=30,TalabKar=65000},new(){HawalaId=2,HawalaNumber=102,RateDate=next,SourceCurrencyId=1,SourceCurrencyCode="AFN",SourceQuotationPriority=30,TalabKar=70000}});
        service.Setup(x=>x.GetRatesAsync(1,It.IsAny<DateTime?>(),It.IsAny<DateTime?>(),It.IsAny<CancellationToken>())).ReturnsAsync((long owner,DateTime? from,DateTime? to,CancellationToken token)=>new[]{new CorrespondentSettlementDailyRateDto{RateDate=from!.Value,SourceCurrencyId=1,TargetCurrencyId=2,ExchangeRate=from==day?65:70}});
        var failed=false;
        service.Setup(x=>x.ConvertHawalasAsync(It.IsAny<ConvertHawalasToSettlementDto>(),It.IsAny<CancellationToken>())).ReturnsAsync((ConvertHawalasToSettlementDto dto,CancellationToken token)=>
        {calls.Add(dto);if(dto.HawalaIds.Single()==2&&!failed){failed=true;throw new InvalidOperationException("second day failed");}return new CorrespondentSettlementResultDto{HawalaCount=1};});
        var display=new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,new TestCurrentTenant{TenantId=0});await display.EnsureLoadedAsync();
        using var services=new ServiceCollection().AddLogging().AddSingleton(display).AddSingleton(service.Object).AddSingleton(Mock.Of<IJSRuntime>()).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            Probe? component=null;
            var root=await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string,object?>
            {[nameof(Probe.IsVisible)]=true,[nameof(Probe.CorrespondentId)]=1L,[nameof(Probe.SelectedHawalas)]=new[]{new HawalaDto{Id=1,HawalaType="HawalaReceive",FromCurrencyId=1,SettlementCurrencyId=2},new(){Id=2,HawalaType="HawalaReceive",FromCurrencyId=1,SettlementCurrencyId=2}},[nameof(Probe.Capture)]=(Action<Probe>)(x=>component=x)}));
            var submit=typeof(CorrespondentSettlementModal).GetMethod("SubmitAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
            Assert.Contains("ثبت تبدیل همین روز",WebUtility.HtmlDecode(root.ToHtmlString()));
            await (Task)submit.Invoke(component,null)!;
            Assert.Equal(2,calls.Count);Assert.Equal(65m,calls[0].HawalaRates.Single().Rate);Assert.Equal(70m,calls[1].HawalaRates.Single().Rate);
            await (Task)submit.Invoke(component,null)!;
            Assert.Equal(3,calls.Count);Assert.Equal(2L,calls[2].HawalaIds.Single());
        });
    }
    public sealed class Probe:CorrespondentSettlementModal{[Parameter]public Action<Probe> Capture{get;set;}=default!;protected override void OnInitialized()=>Capture(this);}
}
