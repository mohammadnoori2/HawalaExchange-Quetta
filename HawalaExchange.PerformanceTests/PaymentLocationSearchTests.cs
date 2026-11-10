using System.Reflection;
using System.Net;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HawalaExchange.PerformanceTests;

public class PaymentLocationSearchTests
{
    [Fact]
    public async Task Search_renders_names_addresses_aliases_and_distinct_ids_for_same_name_without_server_input_events()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PaymentLocationSearchSelect>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Locations"] = new[] { new PaymentLocationDto { Id = 12, Name = "Barchi", Address = "Kabul", Aliases = ["West Kabul"] },
                    new PaymentLocationDto { Id = 13, Name = "Barchi", Address = "Herat" } }, ["Value"] = (long?)13
            }));
            var html = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.Contains("data-account-value=\"13\"", html);
            Assert.Contains("West Kabul", html); Assert.Contains("Kabul", html); Assert.Contains("Herat", html);
            Assert.Contains("\\\"12\\\"", html.Replace("\"", "\\\""));
            Assert.Contains("role=\"combobox\"", html); Assert.Contains("popover=\"manual\"", html);
            Assert.DoesNotContain("oninput=", html);
        });
    }

    [Fact]
    public async Task Selection_commits_only_known_ids_and_clearing_and_rejects_stale_disabled_and_invalid_messages()
    {
        var values = new List<long?>();
        var picker = new Probe { Locations = [new() { Id = 12, Name = "Barchi" }],
            ValueChanged = EventCallback.Factory.Create<long?>(new object(), (long? id) => values.Add(id)) };
        picker.Configure();
        await picker.Select("{\"id\":\"999\",\"revision\":1}");
        await picker.Select("{\"id\":\"12\",\"revision\":2}");
        await picker.Select("{\"id\":\"0\",\"revision\":1}");
        Assert.Equal(new long?[] { 12 }, values);
        picker.Disabled = true;
        await picker.Select("{\"id\":\"0\",\"revision\":3}");
        Assert.Single(values);
        picker.Disabled = false;
        await picker.Select("invalid json");
        await picker.Select("{\"id\":\"0\",\"revision\":4}");
        Assert.Equal(new long?[] { 12, null }, values);
    }

    private class Probe : PaymentLocationSearchSelect
    {
        public void Configure() => OnParametersSet();
        public Task Select(string json) => (Task)typeof(PaymentLocationSearchSelect)
            .GetMethod("HandleSelectionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, [new ChangeEventArgs { Value = json }])!;
    }
}
