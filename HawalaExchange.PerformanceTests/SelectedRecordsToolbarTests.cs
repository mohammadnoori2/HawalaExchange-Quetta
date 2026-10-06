using System.Net;
using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;

namespace HawalaExchange.PerformanceTests;

public sealed class SelectedRecordsToolbarTests
{
    [Fact]
    public async Task Paging_preserves_selection_and_reset_or_stale_messages_do_not_cross_groups()
    {
        var display = await DatesAsync();
        using var services = Services(display, Mock.Of<ISelectedRecordsService>());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Probe? component = null;
            var root = await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Probe.ScopeId)] = "test", [nameof(Probe.ResetKey)] = "group1",
                [nameof(Probe.VisibleIds)] = new long[] { 1, 2 },
                [nameof(Probe.Capture)] = (Action<Probe>)(value => component = value)
            }));
            await component!.UpdateVisibleSelection([1, 99999], [], "group1");
            Assert.Contains("1 ردیف انتخاب‌شده", Html(root.ToHtmlString()));
            await component.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(Probe.VisibleIds)] = new long[] { 3, 4 } }));
            await component.UpdateVisibleSelection([3], [], "group1");
            Assert.Contains("2 ردیف انتخاب‌شده", Html(root.ToHtmlString()));
            await component.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(Probe.ResetKey)] = "group2", [nameof(Probe.VisibleIds)] = new long[] { 5 } }));
            await component.UpdateVisibleSelection([3], [], "group1");
            Assert.Contains("0 ردیف انتخاب‌شده", Html(root.ToHtmlString()));
        });
    }

    [Fact]
    public async Task Share_preview_excludes_sensitive_fields_by_default_and_keeps_received_and_sent_totals_separate()
    {
        var display = await DatesAsync();
        var records = new List<SelectedRecordDto>
        {
            Row(1, "HawalaReceive"), Row(2, "HawalaSend")
        };
        var service = new Mock<ISelectedRecordsService>();
        service.Setup(x => x.ReadAsync(It.IsAny<IReadOnlyCollection<long>>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(records);
        using var services = Services(display, service.Object);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Probe? component = null;
            var root = await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Probe.ScopeId)] = "test", [nameof(Probe.VisibleIds)] = new long[] { 1, 2 },
                [nameof(Probe.SelectAllOnOpen)] = true,
                [nameof(Probe.Capture)] = (Action<Probe>)(value => component = value)
            }));
            var method = typeof(SelectedRecordsToolbar).GetMethod("PreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(component, null)!;
            component!.Refresh();
            var html = Html(root.ToHtmlString());
            Assert.Contains("Sender", html);
            Assert.Contains("100 AFN", html);
            Assert.DoesNotContain("200 AFN", html);
            Assert.DoesNotContain("PRIVATE", html);
            Assert.Contains("https://wa.me/?text=", html);
            Assert.Contains("https://t.me/share/url?url=&text=", html);
            Assert.Contains("noopener noreferrer", html);
            typeof(SelectedRecordsToolbar).GetField("includePhones", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, true);
            component.Refresh();
            Assert.Contains("PRIVATE phone", Html(root.ToHtmlString()));
        });
    }

    [Fact]
    public async Task Hawala_text_matches_requested_order_and_only_existing_notes_and_checked_extras_are_included()
    {
        var display = await DatesAsync();
        display.SetCalendar(true);
        var row = Row(1, "HawalaSend");
        row.Number = "2172"; row.ReferenceNumber = "A44559";
        row.SenderName = "Abdollah Nazari"; row.ReceiverName = "Abdul Jamil";
        row.PaymentLocationName = "Qonduz"; row.Amount = 21560;
        row.CorrespondentName = "Extra correspondent";
        var service = new Mock<ISelectedRecordsService>();
        service.Setup(x => x.ReadAsync(It.IsAny<IReadOnlyCollection<long>>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { row });
        using var services = Services(display, service.Object);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Probe? component = null;
            await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Probe.ScopeId)] = "format", [nameof(Probe.VisibleIds)] = new long[] { 1 },
                [nameof(Probe.SelectAllOnOpen)] = true,
                [nameof(Probe.Capture)] = (Action<Probe>)(value => component = value)
            }));
            await (Task)typeof(SelectedRecordsToolbar).GetMethod("PreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;
            var build = typeof(SelectedRecordsToolbar).GetMethod("BuildText", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var expected = string.Join(Environment.NewLine, new[]
            {
                "نمبر حواله: 2172", "نمبر متفرقه: A44559", "فرستنده: Abdollah Nazari", "گیرنده: Abdul Jamil",
                "محل پرداخت: Qonduz", "مبلغ: 21,560 AFN", "تاریخ: 1405/07/14", "────────────", "تعداد: 1", "مجموع حواله : 21,560 AFN", ""
            });
            Assert.Equal(expected, (string)build.Invoke(component, null)!);
            row.Notes = "  یادداشت موجود  ";
            var text = (string)build.Invoke(component, null)!;
            Assert.Contains("تاریخ: 1405/07/14" + Environment.NewLine + "یاداشت: یادداشت موجود" + Environment.NewLine + "────────────", text);
            row.Notes = "   ";
            foreach (var field in new[] { "includeType", "includeCorrespondent", "includeStatus", "includeIdentity", "includeCommission" })
                typeof(SelectedRecordsToolbar).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, true);
            text = (string)build.Invoke(component, null)!;
            Assert.Contains("نوع:", text); Assert.Contains("Extra correspondent", text); Assert.Contains("وضعیت:", text);
            Assert.Contains("PRIVATE identity", text); Assert.DoesNotContain("یاداشت:", text);
        });
    }

    [Fact]
    public async Task Hidden_operations_keep_the_same_selection_and_notify_conversion_consumer()
    {
        var display = await DatesAsync();
        using var services = Services(display, Mock.Of<ISelectedRecordsService>());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Probe? component = null;
            IReadOnlyList<long> selected = [];
            var root = await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Probe.ScopeId)] = "unified", [nameof(Probe.VisibleIds)] = new long[] { 1, 2 },
                [nameof(Probe.ShowActions)] = false,
                [nameof(Probe.SelectionChanged)] = EventCallback.Factory.Create<IReadOnlyList<long>>(new object(), (IReadOnlyList<long> ids) => selected = ids),
                [nameof(Probe.Capture)] = (Action<Probe>)(value => component = value)
            }));
            await component!.UpdateVisibleSelection([1], [], "");
            Assert.Equal(new long[] { 1 }, selected);
            Assert.DoesNotContain("خروج اکسل", Html(root.ToHtmlString()));
            await component.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(Probe.ShowActions)] = true }));
            Assert.Contains("1 ردیف انتخاب‌شده", Html(root.ToHtmlString()));
            Assert.DoesNotContain("انتخاب همه همین صفحه / گروه", Html(root.ToHtmlString()));
            await component.SetSelectionAsync([1, 2]);
            Assert.Equal(new long[] { 1, 2 }, selected);
            await component.SetSelectionAsync([]);
            Assert.Empty(selected);
        });
    }

    private static SelectedRecordDto Row(long id, string kind) => new()
    {
        Id = id, Number = id.ToString(), Kind = kind, SenderName = "Sender", ReceiverName = "Receiver",
        CurrencyCode = "AFN", Amount = 100, SenderPhone = "PRIVATE phone", SenderIdentity = "PRIVATE identity",
        Notes = "", Status = "Pending", CreatedAt = new DateTime(2026, 10, 6, 8, 0, 0)
    };
    private static string Html(string value) => WebUtility.HtmlDecode(value);
    private static async Task<AppDateDisplay> DatesAsync()
    {
        var display = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 0 });
        await display.EnsureLoadedAsync(); display.SetCalendar(false); return display;
    }
    private static ServiceProvider Services(AppDateDisplay display, ISelectedRecordsService service) =>
        new ServiceCollection().AddLogging().AddSingleton(display).AddSingleton(service)
            .AddSingleton(Mock.Of<IJSRuntime>()).BuildServiceProvider();
    public sealed class Probe : SelectedRecordsToolbar
    {
        [Parameter] public Action<Probe> Capture { get; set; } = default!;
        protected override void OnInitialized() => Capture(this);
        public void Refresh() => StateHasChanged();
    }
}
