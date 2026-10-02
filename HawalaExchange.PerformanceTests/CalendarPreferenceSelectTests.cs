using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.PerformanceTests;

public class CalendarPreferenceSelectTests
{
    [Fact]
    public async Task SelectionCanChangeBothWaysAndSurvivesRerender()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Host? host = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Host.Capture)] = (Action<Host>)(instance => host = instance)
            }));
            Assert.NotNull(host);
            Assert.True(host.Value);
            Assert.Contains("value=\"Persian\" selected", root.ToHtmlString());
            await host.Selector.OnSelectionChanged(new ChangeEventArgs { Value = "Gregorian" });
            Assert.False(host.Value);
            Assert.Contains("value=\"Gregorian\" selected", root.ToHtmlString());
            await host.Selector.OnSelectionChanged(new ChangeEventArgs { Value = "Persian" });
            Assert.True(host.Value);
            Assert.Contains("value=\"Persian\" selected", root.ToHtmlString());
            await host.Selector.OnSelectionChanged(new ChangeEventArgs { Value = "invalid" });
            Assert.True(host.Value);
        });
    }

    public sealed class Host : ComponentBase
    {
        [Parameter] public Action<Host> Capture { get; set; } = default!;
        public bool Value { get; private set; } = true;
        public CalendarPreferenceSelect Selector { get; private set; } = default!;

        protected override void OnInitialized() => Capture(this);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CalendarPreferenceSelect>(0);
            builder.AddAttribute(1, "Value", Value);
            builder.AddAttribute(2, "ValueChanged", EventCallback.Factory.Create<bool>(this, value =>
            {
                Value = value;
                StateHasChanged();
            }));
            builder.AddComponentReferenceCapture(3, instance => Selector = (CalendarPreferenceSelect)instance);
            builder.CloseComponent();
        }
    }
}
