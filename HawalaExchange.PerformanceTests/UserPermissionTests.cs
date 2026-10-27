using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Security;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HawalaExchange.PerformanceTests;

public sealed class UserPermissionTests
{
    [Fact] public void Actions_imply_view_and_unknown_permissions_are_rejected()
    {
        Assert.Equal(new[]{"Customers.Create","Customers.View"},PermissionCatalog.Normalize(["Customers.Create"]).Order().ToArray());
        Assert.Empty(PermissionCatalog.Normalize([]));
        Assert.Throws<InvalidOperationException>(()=>PermissionCatalog.Normalize(["Users.Root"]));
        Assert.DoesNotContain("Users.ManagePermissions",PermissionCatalog.Defaults(["Supervisor"]));
    }
    [Theory]
    [InlineData("/customers/12","Customers.View")]
    [InlineData("/correspondents/12/forwarding-commission","ForwardingCommission.View")]
    [InlineData("/correspondents/12/hawala-import/preview/3","BulkImports.View")]
    [InlineData("/reports/profit-loss","ProfitLoss.View")]
    public void Direct_routes_have_the_correct_module(string route,string key)=>Assert.Equal(key,PermissionCatalog.PagePermission(route));
    [Fact] public void Every_registered_service_operation_has_an_explicit_mapping()
    {
        var assembly=typeof(IHawalaService).Assembly;
        var missing=new List<string>();
        foreach(var pair in PermissionOperationMap.Modules)
        {
            var type=assembly.GetTypes().Single(x=>x.IsInterface && x.Name==pair.Key);
            foreach(var method in type.GetInterfaces().Append(type).SelectMany(x=>x.GetMethods()).Where(x=>!x.IsSpecialName))
            {
                if(pair.Key=="IAuditLogService" && method.Name is "LogAsync" or "CleanupOldLogsAsync")continue;
                try { PermissionOperationMap.Resolve(pair.Key,method.Name); }
                catch(UnauthorizedAccessException) { missing.Add(pair.Key+"."+method.Name); }
            }
        }
        Assert.Empty(missing);
        Assert.Throws<UnauthorizedAccessException>(()=>PermissionOperationMap.Resolve("IHawalaService","NewUnreviewedOperationAsync"));
    }
    [Fact] public void Payment_location_lookup_is_an_explicit_read_operation() =>
        Assert.Equal("Hawalas.View", PermissionOperationMap.Resolve("IHawalaService", "ResolvePaymentLocationAccountAsync"));
    [Fact] public async Task Denied_create_never_invokes_the_business_service_and_allowed_read_returns_its_result()
    {
        var permission=new Mock<IUserPermissionService>();
        permission.SetupGet(x=>x.CurrentPermissions).Returns(new HashSet<string>{"Hawalas.View"});
        permission.Setup(x=>x.RequireAsync(It.IsAny<string>(),It.IsAny<CancellationToken>())).Returns<string,CancellationToken>((key,ct)=>key=="Hawalas.View"?Task.CompletedTask:Task.FromException(new UnauthorizedAccessException()));
        var target=new Mock<IHawalaService>();
        target.Setup(x=>x.GetHawalaByIdAsync(4)).ReturnsAsync(new HawalaDto{Id=4});
        var services=new ServiceCollection();
        services.AddSingleton(permission.Object);
        services.AddSingleton(Mock.Of<IDbContextFactory<ApplicationDbContext>>());
        services.AddSingleton(target.Object); services.ProtectTenantServices();
        using var provider=services.BuildServiceProvider();
        var guarded=provider.GetRequiredService<IHawalaService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>guarded.CreateHawalaAsync(new()));
        target.Verify(x=>x.CreateHawalaAsync(It.IsAny<CreateHawalaDto>()),Times.Never);
        Assert.Equal(4,(await guarded.GetHawalaByIdAsync(4))!.Id);
    }
    [Theory]
    [InlineData("Incoming","HawalaReceive","IncomingCommission")]
    [InlineData("Destination","HawalaSend","DestinationCommission")]
    [InlineData("Forwarding","HawalaReceive","ForwardingCommission")]
    public void Commission_permissions_are_separate(string scope,string type,string expected)=>Assert.Equal(expected,PermissionServiceProxy.CommissionModule(scope,type));
}
