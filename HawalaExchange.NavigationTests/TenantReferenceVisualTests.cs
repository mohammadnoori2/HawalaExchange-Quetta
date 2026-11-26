using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Shared.Modals;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public class TenantReferenceVisualTests
{
    static readonly List<CurrencyDto> Currencies=[new(){Id=1,Code="AFN",Name="افغانی",IsActive=true,QuotationPriority=60},new(){Id=2,Code="USD",Name="دالر",IsActive=true,QuotationPriority=20},new(){Id=3,Code="PKR",Name="کلدار",IsActive=true,QuotationPriority=40}];
    static readonly List<AccountDto> Accounts=[new(){Id=1,AccountCode="CASH",AccountName="صندوق",AccountType="Cash"},new(){Id=2,AccountCode="CUS-1",AccountName="محمد نوری",AccountType="Customer"},new(){Id=3,AccountCode="AG-006",AccountName="نماینده: زاهدی",AccountType="Correspondent"}];
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Field(object component,Type owner,string name,object value)=>owner.GetField(name,Hidden)!.SetValue(component,value);
    static void Property(object component,Type owner,string name,object value)=>owner.GetProperty(name,Hidden)!.SetValue(component,value);

    [Theory]
    [InlineData("deposit")]
    [InlineData("exchange")]
    [InlineData("execution")]
    [InlineData("pending")]
    [InlineData("correspondents")]
    public async Task Render_real_tenant_components_with_sample_data_without_financial_writes(string scenario)
    {
        var collection=new ServiceCollection().AddLogging();
        foreach(var type in typeof(MoneyExchangeModal).Assembly.GetTypes().Where(t=>typeof(IComponent).IsAssignableFrom(t))
            .SelectMany(t=>t.GetProperties(Hidden|BindingFlags.Public)).Where(p=>p.GetCustomAttribute<InjectAttribute>() is not null&&p.PropertyType.IsInterface).Select(p=>p.PropertyType).Distinct())
        {var mock=(Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;collection.AddSingleton(type,mock.Object);}
        var balances=new Mock<IBalanceService>();balances.Setup(x=>x.GetAccountBalanceAsync(It.IsAny<long>(),It.IsAny<DateTime?>())).ReturnsAsync(new[]{new BalanceDto{CurrencyId=1,CurrencyCode="AFN",Balance=-4695940},new(){CurrencyId=2,CurrencyCode="USD",Balance=-77771}});
        var permissions=new Mock<IUserPermissionService>();permissions.Setup(x=>x.Can(It.IsAny<string>())).Returns(true);
        var company=new Mock<ICompanySettingService>();company.Setup(x=>x.GetAsync()).ReturnsAsync(new CompanySettingDto());
        var hawalas=new Mock<IHawalaService>();hawalas.Setup(x=>x.GetPaymentHistoryAsync(It.IsAny<int>(),It.IsAny<int>())).ReturnsAsync(new PaginatedResult<HawalaPaymentBatchDto>{Items=[],TotalPages=1});
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().Options;var tenant=Mock.Of<ICurrentTenant>();
        collection.AddSingleton(hawalas.Object).AddSingleton(new UiConnectionState()).AddSingleton(Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Components.Server.CircuitOptions())).AddSingleton(balances.Object).AddSingleton(permissions.Object).AddSingleton(company.Object).AddSingleton(options).AddSingleton(new ApplicationDbContext(options,tenant))
            .AddSingleton(new AppDateDisplay(options,tenant)).AddSingleton(new UiReferenceDataCache(tenant,Mock.Of<ICurrencyService>(),Mock.Of<IPaymentLocationService>(),company.Object)).AddSingleton<NavigationManager>(new TestNavigation());
        using var services=collection.BuildServiceProvider();await using var renderer=new PreviewRenderer(services,services.GetRequiredService<ILoggerFactory>());
        var renderType=scenario switch{"deposit"=>typeof(DepositPreview),"exchange"=>typeof(ExchangePreview),"correspondents"=>typeof(CorrespondentsPreview),_=>typeof(PendingPreview)};
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var parameters=scenario is "deposit" or "exchange"?new Dictionary<string,object?>{{"IsVisible",true}}:scenario=="execution"?new Dictionary<string,object?>{{"Execution",true}}:new();
            var view=renderer.BeginRenderingComponent(renderType,ParameterView.FromDictionary(parameters));await view.QuiescenceTask;
            var html=view.ToHtmlString();Assert.DoesNotContain("خطا در بارگذاری",System.Net.WebUtility.HtmlDecode(html));
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!Directory.Exists(Path.Combine(root.FullName,"HawalaExchange.Web")))root=root.Parent;
            var output=Path.Combine(root!.FullName,"HawalaExchange.Web","obj","tenant-reference-qa");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,scenario+".html"),html);
        });
    }
    public sealed class DepositPreview:DepositWithdrawModal
    {
        protected override Task OnParametersSetAsync(){var owner=typeof(DepositWithdrawModal);Property(this,owner,"CurrentOperation",new CreateAccountMoneyOperationDto{OperationType="Deposit",CurrencyId=1,CashOrBankAccountId=1,AccountId=2});Property(this,owner,"CurrenciesList",Currencies);Property(this,owner,"AccountsList",Accounts);Property(this,owner,"CashBankAccounts",Accounts.Take(1).ToList());Property(this,owner,"SelectableAccounts",Accounts.Skip(1).ToList());Property(this,owner,"IsDataLoading",false);return Task.CompletedTask;}
    }
    public sealed class ExchangePreview:MoneyExchangeModal
    {
        protected override Task OnParametersSetAsync(){var owner=typeof(MoneyExchangeModal);Property(this,owner,"CurrentExchange",new CreateMoneyExchangeOperationDto{OperationType="Treasury",FromCurrencyId=1,ToCurrencyId=2,FromAccountId=1,ToAccountId=1,Description="ثبت تبدیل پول"});Property(this,owner,"CurrenciesList",Currencies);Property(this,owner,"AccountsList",Accounts);Property(this,owner,"IsDataLoading",false);return Task.CompletedTask;}
    }
    public sealed class PendingPreview:HawalaExchange.Web.Components.Pages.HawalaTransactions.PendingHawalas
    {
        [Parameter]public bool Execution{get;set;}
        protected override Task OnInitializedAsync()=>Task.CompletedTask;
        protected override Task OnParametersSetAsync(){var owner=typeof(HawalaExchange.Web.Components.Pages.HawalaTransactions.PendingHawalas);var rows=Enumerable.Range(1,14).Select(i=>new HawalaDto{Id=i,Number=767-i,ReferenceNumber="A"+(55895+i),SenderName="Farhad Hakimi",ReceiverName="Nelofar Nizami",FromAmount=17200+i*150,FromCurrencyId=1,FromCurrencyCode="AFN",CorrespondentId=1,CorrespondentName="کویته",CreatedAt=new DateTime(2026,11,24,4,57,0),Status="Pending",HawalaType="HawalaReceive"}).ToList();Field(this,owner,"hawalas",rows);Field(this,owner,"currencies",Currencies);Field(this,owner,"accounts",Accounts);Field(this,owner,"correspondents",new List<CorrespondentDto>{new(){Id=1,Name="کویته"}});Field(this,owner,"totalCount",38);Field(this,owner,"totalFromAmount",333569m);Field(this,owner,"showPayModal",Execution);Field(this,owner,"payHawala",rows[0]);Field(this,owner,"payHawalaNumber",766L);Field(this,owner,"payDetails",new PayHawalaDto{ReceiverName="Nelofar Nizami",AgentCommissionAmount=200,AgentCommissionCurrencyId=1});return Task.CompletedTask;}
    }
    public sealed class CorrespondentsPreview:HawalaExchange.Web.Components.Pages.Correspondents.Correspondents
    {
        protected override Task OnInitializedAsync(){var owner=typeof(HawalaExchange.Web.Components.Pages.Correspondents.Correspondents);var names=new[]{"برچی کابل","عبدالله ادلید","کویته کویته","هادی محمدی کابل","سلمان نظری غزنی","زاهدی سنگماشه","مصطفی اکبری غور","بشیر صافی کابل","جوادی هرات","متفرقه کابل","نمایندگی جدید"};var rows=names.Select((name,i)=>new CorrespondentDto{Id=i+1,Code="AG-"+(i+1).ToString("000"),Name=name,Country="افغانستان",City="کابل"}).ToList();Field(this,owner,"correspondents",rows);Field(this,owner,"basefilteredCorrespondents",rows);Field(this,owner,"balancesReady",true);Field(this,owner,"listBalances",rows.ToDictionary(x=>x.Id,x=>new List<BalanceDto>{new(){CurrencyCode="AFN",Balance=(x.Id%3==0?-1:1)*x.Id*150000},new(){CurrencyCode="USD",Balance=(x.Id%3==0?-1:1)*x.Id*1250},new(){CurrencyCode="PKR",Balance=x.Id==2?70000:0}}));return Task.CompletedTask;}
    }
    private sealed class PreviewRenderer(IServiceProvider services,ILoggerFactory logger):StaticHtmlRenderer(services,logger)
    {protected override IComponent ResolveComponentForRenderMode(Type type,int? parentId,IComponentActivator activator,IComponentRenderMode mode)=>activator.CreateInstance(type);}
    private sealed class TestNavigation:NavigationManager
    {public TestNavigation()=>Initialize("http://localhost/","http://localhost/hawala-pending");protected override void NavigateToCore(string uri,bool forceLoad){}}
}
