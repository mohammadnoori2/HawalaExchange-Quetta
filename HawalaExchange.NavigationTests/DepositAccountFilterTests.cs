using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Pages.Deposit;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;
public class DepositAccountFilterTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    [Fact]
    public async Task Account_ids_separate_duplicate_names_preserve_refresh_and_clear_all_filters()
    {
        var rows=new List<AccountMoneyOperationDto> {
            new(){Id=1,AccountId=20,AccountName="متفرقه کابل ",CashOrBankAccountId=1,CurrencyId=1,OperationType="Withdraw"},
            new(){Id=2,AccountId=21,AccountName="متفرقه کابل ",CashOrBankAccountId=1,CurrencyId=1,OperationType="Withdraw"},
            new(){Id=3,AccountId=20,AccountName="نام ویرایش‌شده",CashOrBankAccountId=2,CurrencyId=1,OperationType="Withdraw"}
        };
        var operations=new Mock<IAccountMoneyOperationService>();operations.Setup(x=>x.GetAllAsync()).ReturnsAsync(rows);
        using var services=new ServiceCollection().AddLogging()
            .AddSingleton(operations.Object).AddSingleton(Mock.Of<IAccountService>()).AddSingleton(Mock.Of<ICurrencyService>())
            .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,Mock.Of<ICurrentTenant>())).BuildServiceProvider();
        using var renderer=new LifecycleRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            Probe? page=null;
            await renderer.Render(typeof(Probe),ParameterView.FromDictionary(new Dictionary<string,object?> { ["Capture"]=(Action<Probe>)(x=>page=x) }));
            await Call(page!,"LoadOperations");
            Property("filterAccountId").SetValue(page,20L);
            Assert.Equal(new long[]{1,3},Results(page!).Select(x=>x.Id));
            Property("filterCashAccountId").SetValue(page,1L);
            Assert.Equal(1,Assert.Single(Results(page!)).Id);
            await Call(page!,"LoadOperations");
            Assert.Equal(1,Assert.Single(Results(page!)).Id);
            typeof(DepositWithdraw).GetMethod("ClearSearch",Hidden)!.Invoke(page,null);
            Assert.Equal(3,Results(page!).Count);
        });
    }
    static PropertyInfo Property(string name)=>typeof(DepositWithdraw).GetProperty(name,Hidden)!;
    static List<AccountMoneyOperationDto> Results(Probe page)=>(List<AccountMoneyOperationDto>)Property("filteredOperations").GetValue(page)!;
    static Task Call(Probe page,string method)=>(Task)typeof(DepositWithdraw).GetMethod(method,Hidden)!.Invoke(page,null)!;
    private sealed class LifecycleRenderer(IServiceProvider services, ILoggerFactory logging):Renderer(services,logging)
    {
        public override Dispatcher Dispatcher {get;}=Dispatcher.CreateDefault();
        public Task Render(Type type,ParameterView parameters)=>RenderRootComponentAsync(AssignRootComponentId(InstantiateComponent(type)),parameters);
        protected override Task UpdateDisplayAsync(in RenderBatch batch)=>Task.CompletedTask;
        protected override void HandleException(Exception exception)=>throw exception;
        protected override IComponent ResolveComponentForRenderMode(Type type,int? parentId,IComponentActivator activator,IComponentRenderMode mode)=>activator.CreateInstance(type);
    }
    public sealed class Probe:DepositWithdraw
    {
        [Parameter] public Action<Probe> Capture {get;set;}=default!;
        protected override void OnInitialized()=>Capture(this);
        protected override Task OnInitializedAsync()=>Task.CompletedTask;
        protected override void BuildRenderTree(RenderTreeBuilder builder){}
    }
}
