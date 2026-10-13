using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Security;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moq;

namespace HawalaExchange.PerformanceTests;

public sealed class UserPermissionUiTests
{
    [Fact] public async Task Hidden_menu_content_appears_when_granted_and_disappears_on_live_revoke()
    {
        var rights=new HashSet<string>(); var permission=new Mock<IUserPermissionService>();
        permission.Setup(x=>x.Can(It.IsAny<string>())).Returns<string>(rights.Contains);
        using var services=new ServiceCollection().AddLogging().AddSingleton(permission.Object).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async ()=>
        {
            var root=await renderer.RenderComponentAsync<PermissionView>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { [nameof(PermissionView.Permission)]="Customers.View",[nameof(PermissionView.ChildContent)]=(RenderFragment)(b=>b.AddContent(0,"CUSTOMER MENU")) }));
            Assert.DoesNotContain("CUSTOMER MENU",root.ToHtmlString());
            rights.Add("Customers.View"); permission.Raise(x=>x.Changed+=null); Assert.Contains("CUSTOMER MENU",root.ToHtmlString());
            rights.Clear(); permission.Raise(x=>x.Changed+=null); Assert.DoesNotContain("CUSTOMER MENU",root.ToHtmlString());
        });
    }
    [Fact] public async Task Group_is_hidden_when_none_of_its_children_are_allowed()
    {
        var permission=new Mock<IUserPermissionService>(); permission.Setup(x=>x.Can(It.IsAny<string>())).Returns(false);
        using var services=new ServiceCollection().AddLogging().AddSingleton(permission.Object).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async ()=>
        {
            var root=await renderer.RenderComponentAsync<PermissionView>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { [nameof(PermissionView.Any)]="Ledger.View,Capital.View,Expenses.View",[nameof(PermissionView.ChildContent)]=(RenderFragment)(b=>b.AddContent(0,"ACCOUNTING GROUP")) }));
            Assert.DoesNotContain("ACCOUNTING GROUP",root.ToHtmlString());
        });
    }
    [Fact] public async Task Editor_lists_every_module_and_disables_permissions_the_actor_does_not_have()
    {
        var permission=new Mock<IUserPermissionService>(); permission.Setup(x=>x.Can(It.IsAny<string>())).Returns(false);
        using var services=new ServiceCollection().AddLogging().AddSingleton(permission.Object).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async ()=>
        {
            var root=await renderer.RenderComponentAsync<UserPermissionEditor>(); var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            foreach(var module in PermissionCatalog.Modules)Assert.Contains(module.Name,html);
            Assert.Contains("disabled",html);
        });
    }
}
