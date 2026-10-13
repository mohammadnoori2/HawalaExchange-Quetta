using System.Reflection;
using System.Runtime.ExceptionServices;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HawalaExchange.Infrastructure.Services;

internal static class AuthorizedServiceCall
{
    internal static readonly AsyncLocal<int> Depth=new();
}
public class PermissionServiceProxy : DispatchProxy, IDisposable
{
    private object target=null!;
    private string service="";
    private IUserPermissionService permissions=null!;
    private IDbContextFactory<ApplicationDbContext> factory=null!;
    public static object Wrap(Type type,object target,IServiceProvider sp)
    {
        var proxy=(PermissionServiceProxy)Create(type,typeof(PermissionServiceProxy));
        proxy.target=target; proxy.service=type.Name;
        proxy.permissions=sp.GetRequiredService<IUserPermissionService>();
        proxy.factory=sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        return proxy;
    }
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method is null)throw new UnauthorizedAccessException();
        args ??= [];
        if(method.Name is "add_AlertsChanged" or "remove_AlertsChanged")return Call(method,args);
        if(method.ReturnType==typeof(Task))return RunAsync(method,args);
        if(method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition()==typeof(Task<>))
            return GetType().BaseType!.GetMethod(nameof(RunResultAsync),BindingFlags.NonPublic|BindingFlags.Instance)!.MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]).Invoke(this,[method,args]);
        throw new UnauthorizedAccessException("Unsupported service boundary");
    }
    private async Task AuthorizeAsync(MethodInfo method,object?[] args)
    {
        if(AuthorizedServiceCall.Depth.Value>0)return; // Internal consequences of an already-authorized operation.
        string? module=null;
        var name=method.Name;
        await permissions.RefreshAsync(true);
        if(service=="ICompanySettingService" && name=="GetAsync" && permissions.CurrentPermissions.Count>0)return;
        // Reference lists are required by transaction forms; section/detail access is checked separately.
        if(service is "IBranchService" or "ICurrencyService" or "IPaymentLocationService" or "ICustomerService" or "ICorrespondentService" or "IAccountService"
            && name is "GetAllAsync" or "GetActiveAsync" or "GetActiveBranchesAsync" or "GetActiveCurrenciesAsync" or "GetActiveAccountsAsync"
            && permissions.CurrentPermissions.Any(x=>x.EndsWith(".View",StringComparison.Ordinal)))return;
        if(service=="IAuditLogService" && name=="LogAsync" && permissions.CurrentPermissions.Count>0)return;
        if(service=="ICorrespondentCommissionService")
        {
            if(args.FirstOrDefault() is CorrespondentCommissionPreviewRequestDto request)module=CommissionModule(request.CommissionScope,request.HawalaType);
            else if(name is "ReverseAsync" or "GetDetailsAsync")
            {
                await using var db=await factory.CreateDbContextAsync();
                var id=(long)args[0]!;
                var batch=await db.CorrespondentCommissionBatches.AsNoTracking().Where(x=>x.Id==id).Select(x=>new {x.CommissionScope,HawalaType=x.Items.Select(i=>i.Hawala.HawalaType).FirstOrDefault()}).SingleOrDefaultAsync();
                if(batch is null)throw new InvalidOperationException("کمیشن یافت نشد.");
                module=CommissionModule(batch.CommissionScope,batch.HawalaType ?? "HawalaReceive");
            }
            else if(name=="GetHistoryAsync")
            {
                if(new[]{"IncomingCommission.View","DestinationCommission.View","ForwardingCommission.View"}.Any(permissions.Can))return;
            }
        }
        if(service=="IFinancialReportService")module=name switch {"GetDashboardSummaryAsync"=>"Dashboard","GetBalanceSheetAsync"=>"BalanceSheet","GetProfitLossStatementAsync"=>"ProfitLoss",_=>null};
        if(service=="ISelectedRecordsService")module=args.Length>1 && args[1] is true ? "Transactions":"Hawalas";
        if(service=="ISelectedRecordsService" && module=="Hawalas" && !permissions.Can("Hawalas.Export")
            && name is "ReadAsync" or "ExportAsync" or "ExportInputFormatAsync")
        {
            var ids=((IReadOnlyCollection<long>)args[0]!).Distinct().ToArray();
            await using var db=await factory.CreateDbContextAsync();
            if(ids.Length>0 && permissions.Can("BulkImports.Export"))
            {
                var imported=await db.HawalaImportRows.AsNoTracking().Where(x=>
                    x.HawalaId.HasValue && ids.Contains(x.HawalaId.Value) || x.GeneratedSendHawalaId.HasValue && ids.Contains(x.GeneratedSendHawalaId.Value))
                    .Select(x=>new{x.HawalaId,x.GeneratedSendHawalaId}).ToListAsync();
                var linked=imported.SelectMany(x=>new[]{x.HawalaId,x.GeneratedSendHawalaId}).Where(x=>x.HasValue).Select(x=>x!.Value).ToHashSet();
                if(ids.All(linked.Contains))return;
            }
            if(ids.Length>0 && permissions.Can("HawalaExecution.Export") && await db.Hawalas.CountAsync(x=>
                ids.Contains(x.Id) && x.HawalaType=="HawalaReceive" && (x.Status=="Pending" || x.Status=="Paid"))==ids.Length)return;
        }
        if(service=="IExportService")
        {
            module=args[0] switch
            {
                ExportFilterDto filter=>filter.AccountId.HasValue?"AccountReports":filter.CorrespondentId.HasValue?"CorrespondentReports":"CustomerReports",
                CustomerActivityReportDto report=>report.AccountId.HasValue?"AccountReports":report.CorrespondentId.HasValue?"CorrespondentReports":"CustomerReports",
                _=>throw new UnauthorizedAccessException()
            };
        }
        if(service=="IBalanceService")module=name.Contains("Customer")?"Customers":name.Contains("Correspondent")?"Correspondents":"Accounts";
        if(service=="IAccountService" && name=="GetByReferenceAsync")module=(args[0] as string) switch {"Customer"=>"Customers","Correspondent"=>"Correspondents",_=>"Accounts"};
        if((service=="IJournalService" && name is "GetAccountOperationsAsync" or "GetAccountOperationsPageAsync" or "GetAccountOperationDetailsAsync")
            || (service=="IBalanceService" && name=="GetAccountBalanceAsync"))
        {
            var id=args[0] is AccountOperationsFilterDto filter ? filter.AccountId:(long)args[0]!;
            await using var db=await factory.CreateDbContextAsync();
            var owner=await db.Accounts.AsNoTracking().Where(x=>x.Id==id).Select(x=>new{x.CustomerId,x.CorrespondentId}).SingleOrDefaultAsync();
            if(owner is null)throw new InvalidOperationException("حساب یافت نشد.");
            module=owner.CustomerId.HasValue?"Customers":owner.CorrespondentId.HasValue?"Correspondents":"Accounts";
        }
        if(service=="IHawalaService" && name=="GetCorrespondentRangeAsync")module="CorrespondentReports";
        if(service=="IHawalaService" && name=="GetHawalasAsync" && args[0] is HawalaFilterDto { Status:"Pending",HawalaType:"HawalaReceive" })module="HawalaExecution";
        if(service=="IHawalaService" && name=="GetHawalaByIdAsync" && !permissions.Can("Hawalas.View") && permissions.Can("HawalaExecution.View"))
        {
            await using var db=await factory.CreateDbContextAsync();
            if(await db.Hawalas.AnyAsync(x=>x.Id==(long)args[0]! && x.HawalaType=="HawalaReceive" && x.Status=="Pending"))module="HawalaExecution";
        }
        if(service=="IHawalaService")
        {
            if(args[0] is CreateHawalaDto {Status:"Paid",HawalaType:"HawalaReceive"}
                || args.Any(x=>x is UpdateHawalaDto {Status:"Paid"}))await permissions.RequireAsync("HawalaExecution.Execute");
            if(args[0] is IReadOnlyCollection<CreateHawalaDto> items && items.Any(x=>x.Status=="Paid" && x.HawalaType=="HawalaReceive"))await permissions.RequireAsync("HawalaExecution.Execute");
        }
        var key=PermissionOperationMap.Resolve(service,name,module);
        await permissions.RequireAsync(key);
    }
    public static string CommissionModule(string scope,string hawalaType)=>scope=="Forwarding" ? "ForwardingCommission":scope=="Destination" || hawalaType=="HawalaSend" && scope!="Origin" ? "DestinationCommission":"IncomingCommission";
    private object? Call(MethodInfo method,object?[] args)
    {
        try { return method.Invoke(target,args); }
        catch(TargetInvocationException ex) when(ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private async Task RunAsync(MethodInfo method,object?[] args)
    {
        await AuthorizeAsync(method,args); AuthorizedServiceCall.Depth.Value++;
        try { await (Task)Call(method,args)!; }
        finally { AuthorizedServiceCall.Depth.Value--; }
    }
    private async Task<T> RunResultAsync<T>(MethodInfo method,object?[] args)
    {
        await AuthorizeAsync(method,args); AuthorizedServiceCall.Depth.Value++;
        try
        {
            var result=await (Task<T>)Call(method,args)!;
            if(service=="ICorrespondentCommissionService" && method.Name=="GetHistoryAsync" && result is IReadOnlyList<CorrespondentCommissionBatchDto> batches)
                return (T)(object)batches.Where(x=>permissions.Can(CommissionModule(x.CommissionScope,x.HawalaType)+".View")).ToArray();
            return result;
        }
        finally { AuthorizedServiceCall.Depth.Value--; }
    }
    public void Dispose() { if(target is IDisposable disposable)disposable.Dispose(); }
}

public static class PermissionServiceRegistration
{
    public static void ProtectTenantServices(this IServiceCollection services)
    {
        foreach(var descriptor in services.Where(x=>PermissionOperationMap.Modules.ContainsKey(x.ServiceType.Name)).ToArray())
        {
            services.Remove(descriptor);
            services.Add(new ServiceDescriptor(descriptor.ServiceType,sp=>
            {
                var target=descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp,descriptor.ImplementationType!);
                return PermissionServiceProxy.Wrap(descriptor.ServiceType,target,sp);
            },descriptor.Lifetime));
        }
    }
}
