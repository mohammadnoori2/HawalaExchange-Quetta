using System.Security.Claims;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.PerformanceTests;

public class DateAwareComponentTests
{
    [Fact]
    public async Task RenderingPreservesAuthenticationAndEditContextCascadesAndDirectParameters()
    {
        var display = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,
            new TestCurrentTenant { TenantId = 0 });
        using var services = new ServiceCollection().AddLogging().AddSingleton(display).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var auth = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "Calendar user")], "test"))));
        var editContext = new EditContext(new object());
        Probe? probe = null;

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Host.Authentication)] = auth,
                [nameof(Host.Context)] = editContext,
                [nameof(Host.Capture)] = (Action<Probe>)(component => probe = component)
            }));
            Assert.NotNull(probe);
            Assert.Same(auth, probe.Authentication);
            Assert.Same(editContext, probe.Context);
            Assert.Equal("initial", probe.Value);
            Assert.True(probe.InitializedWithAllParameters);
            Assert.Contains("Calendar user|initial|form", root.ToHtmlString());

            await probe.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Probe.Value)] = "updated"
            }));
            Assert.Same(auth, probe.Authentication);
            Assert.Same(editContext, probe.Context);
            Assert.Contains("Calendar user|updated|form", root.ToHtmlString());
        });
    }

    public sealed class Host : ComponentBase
    {
        [Parameter] public Task<AuthenticationState> Authentication { get; set; } = default!;
        [Parameter] public EditContext Context { get; set; } = default!;
        [Parameter] public Action<Probe> Capture { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", Authentication);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(outer =>
            {
                outer.OpenComponent<CascadingValue<EditContext>>(0);
                outer.AddAttribute(1, "Value", Context);
                outer.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
                {
                    inner.OpenComponent<Probe>(0);
                    inner.AddAttribute(1, nameof(Probe.Value), "initial");
                    inner.AddComponentReferenceCapture(2, instance => Capture((Probe)instance));
                    inner.CloseComponent();
                }));
                outer.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    public sealed class Probe : DateAwareComponent
    {
        [CascadingParameter] public Task<AuthenticationState>? Authentication { get; set; }
        [CascadingParameter] public EditContext? Context { get; set; }
        [Parameter] public string Value { get; set; } = string.Empty;
        public bool InitializedWithAllParameters { get; private set; }

        protected override void OnInitialized() => InitializedWithAllParameters =
            Authentication != null && Context != null && Value == "initial";

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0,
            $"{Authentication?.Result.User.Identity?.Name}|{Value}|{(Context != null ? "form" : "no form")}");
    }
}
