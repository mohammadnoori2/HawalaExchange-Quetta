using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Shared.Modals;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;
public class HawalaCompactFinanceTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Model(CreateHawalaModal modal,CreateHawalaDto value)=>typeof(CreateHawalaModal).GetProperty("CurrentHawala",Hidden)!.SetValue(modal,value);
    static void Call(CreateHawalaModal modal,string name,params object[] args)=>typeof(CreateHawalaModal).GetMethod(name,Hidden)!.Invoke(modal,args);
    static bool Expanded(CreateHawalaModal modal)=>(bool)typeof(CreateHawalaModal).GetField("showCurrencyConversion",Hidden)!.GetValue(modal)!;
    [Fact]
    public void Without_conversion_currency_and_amount_follow_source()
    {
        var modal=new CreateHawalaModal();var model=new CreateHawalaDto {FromCurrencyId=1,ToCurrencyId=1,FromAmount=100,ToAmount=100,ExchangeRate=1};Model(modal,model);
        Call(modal,"OnFromCurrencyChanged",new ChangeEventArgs {Value="2"});
        Assert.Equal(2,model.ToCurrencyId);Assert.Equal(1,model.ExchangeRate);
        Call(modal,"OnFromAmountChanged",525m);Assert.Equal(525,model.ToAmount);Assert.False(Expanded(modal));
    }
    [Fact]
    public void Editing_foreign_currency_opens_conversion_and_switching_off_restores_same_currency()
    {
        var modal=new CreateHawalaModal {IsEditMode=true};var model=new CreateHawalaDto {FromCurrencyId=1,ToCurrencyId=2,FromAmount=6500,ToAmount=100,ExchangeRate=65};Model(modal,model);
        Call(modal,"InitializeHawalaConversion");Assert.True(Expanded(modal));Assert.Equal(100,model.ToAmount);Assert.Equal(65,model.ExchangeRate);
        Call(modal,"OnConversionChanged",new ChangeEventArgs {Value=false});
        Assert.False(Expanded(modal));Assert.Equal(1,model.ToCurrencyId);Assert.Equal(6500,model.ToAmount);Assert.Equal(1,model.ExchangeRate);
    }
    [Fact]
    public void New_foreign_currency_pair_requires_a_new_rate()
    {
        var modal=new CreateHawalaModal();var model=new CreateHawalaDto {FromCurrencyId=1,ToCurrencyId=1,FromAmount=100,ToAmount=100,ExchangeRate=1};Model(modal,model);
        Call(modal,"OnConversionChanged",new ChangeEventArgs {Value=true});
        Call(modal,"OnToCurrencyChanged",new ChangeEventArgs {Value="2"});
        Assert.True(Expanded(modal));Assert.Null(model.ExchangeRate);Assert.Null(model.ToAmount);
    }
    [Theory]
    [InlineData("HawalaSend","Paid",false)]
    [InlineData("HawalaReceive","Paid",false)]
    [InlineData("HawalaReceive","Paid",true)]
    [InlineData("HawalaReceive","Pending",false)]
    public async Task Render_actual_default_form_for_layout_verification(string type,string status,bool referral)
    {
        var collection=new ServiceCollection().AddLogging();
        foreach(var componentType in new[]{typeof(CreateHawalaModal),typeof(AccountDetailsModal)})
        for(var t=componentType;t is not null;t=t.BaseType)
            foreach(var property in t.GetProperties(Hidden|BindingFlags.Public|BindingFlags.DeclaredOnly).Where(p=>p.GetCustomAttribute<InjectAttribute>() is not null && p.PropertyType.IsInterface))
            {var mock=(Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(property.PropertyType))!;collection.AddSingleton(property.PropertyType,mock.Object);}
        var permissions=new Mock<IUserPermissionService>();permissions.Setup(x=>x.Can(It.IsAny<string>())).Returns(true);
        using var services=collection.AddSingleton(Mock.Of<IFinancialReceiptPreview>()).AddSingleton(permissions.Object).AddSingleton<NavigationManager>(new TestNavigation())
            .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,Mock.Of<ICurrentTenant>())).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var rendered=await renderer.RenderComponentAsync<LayoutProbe>(ParameterView.FromDictionary(new Dictionary<string,object?> { ["IsVisible"]=true,["Type"]=type,["Status"]=status,["Referral"]=referral }));
            var html=rendered.ToHtmlString();Assert.Contains("data-live-conversion",html);Assert.Contains("نام فرستنده",html);
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null && !Directory.Exists(Path.Combine(root.FullName,"HawalaExchange.Web")))root=root.Parent;
            var output=Path.Combine(root!.FullName,"HawalaExchange.Web","obj","compact-finance-qa");Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output,type+"-"+status+(referral?"-Referral":"")+".html"),html);
        });
    }
    public sealed class LayoutProbe:CreateHawalaModal
    {
        [Parameter] public string Type {get;set;}="HawalaSend";
        [Parameter] public string Status {get;set;}="Paid";
        [Parameter] public bool Referral {get;set;}
        protected override Task OnParametersSetAsync()
        {
            Model(this,new(){HawalaType=Type,Status=Status,FromCurrencyId=1,ToCurrencyId=1,FromAmount=17100,ToAmount=17100,ExchangeRate=1,CommissionAmount=100,CommissionCurrencyId=1,AgentCommissionAmount=150,AgentCommissionCurrencyId=1,SenderName="Sender",ReceiverName="Receiver",Number=44,FromAccountId=Referral?10:null,GeneratedSendHawalaNumber=2});
            typeof(CreateHawalaModal).GetProperty("isDataLoading",Hidden)!.SetValue(this,false);
            typeof(CreateHawalaModal).GetProperty("CurrenciesList",Hidden)!.SetValue(this,new List<CurrencyDto>{new(){Id=1,Code="AFN",Name="افغانی",QuotationPriority=2},new(){Id=2,Code="USD",Name="دالر",QuotationPriority=1}});
            if(Referral)typeof(CreateHawalaModal).GetProperty("AccountsList",Hidden)!.SetValue(this,new List<AccountDto>{new(){Id=10,AccountName="نمایندگی",AccountType="Correspondent"}});
            Call(this,"InitializeHawalaConversion");return Task.CompletedTask;
        }
    }
    private sealed class TestNavigation:NavigationManager
    {
        public TestNavigation()=>Initialize("http://localhost/","http://localhost/hawala-transactions");
        protected override void NavigateToCore(string uri,bool forceLoad){}
    }
}
