using System.Net;
using System.Text.RegularExpressions;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HawalaExchange.PerformanceTests;

public sealed class TenantActivityChartTests
{
    private static PlatformTenantActivity Row(long id, long count, string? name = null) =>
        new(id, name ?? $"Exchange {id}", false, count, count / 2, count / 3, count / 4, null);

    private static async Task Render(IReadOnlyList<PlatformTenantActivity> rows, Func<Probe, Func<string>, Task> run,
        Action<long>? selected = null)
    {
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Probe? chart = null;
            var root = await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Probe.Rows)] = rows, [nameof(Probe.Capture)] = (Action<Probe>)(x => chart = x)
            }));
            Assert.NotNull(chart);
            Assert.DoesNotContain("<script>", root.ToHtmlString());
            await run(chart, () => WebUtility.HtmlDecode(root.ToHtmlString()));
        });
    }

    [Theory]
    [InlineData(100)] [InlineData(500)] [InlineData(1000)]
    public Task Hundreds_of_tenants_render_only_one_page_and_keep_a_fixed_scale(int count) =>
        Render(Enumerable.Range(1, count).Select(i => Row(i, i)).ToArray(), async (chart, html) =>
        {
            Assert.Equal(10, Regex.Matches(html(), "data-tenant-bar=").Count);
            Assert.Contains($"data-tenant-bar=\"{count}\"", html());
            Assert.DoesNotContain($"data-tenant-bar=\"{count - 10}\"", html());
            Assert.Contains("width:100%", html());
            Assert.Contains("<th scope=\"col\">نام صرافی</th>", html());
            Assert.Contains("<th scope=\"col\">مقدار فعالیت</th>", html());
            Assert.Contains("role=\"meter\"", html());
            Assert.Contains($"href=\"/platform/tenants/{count}/activity\"", html());
            chart.Page(2);
            Assert.Contains($"data-tenant-bar=\"{count - 10}\"", html());
            Assert.DoesNotContain("width:100%", html());
            chart.Page(100000);
            Assert.Equal(10, Regex.Matches(html(), "data-tenant-bar=").Count);
            Assert.Contains("data-tenant-bar=\"1\"", html());
            await Task.CompletedTask;
        });

    [Fact]
    public Task Search_finds_any_tenant_and_keeps_global_scale_and_share_denominator() =>
        Render([Row(1, 100, "Big Exchange"), Row(2, 25, "Needle")], (chart, html) =>
        {
            chart.Search("needle");
            Assert.Single(Regex.Matches(html(), "data-tenant-bar="));
            Assert.Contains("width:25%", html()); Assert.Contains("20%", html());
            chart.Search("no match"); Assert.Contains("صرافی‌ای با این جستجو یافت نشد", html());
            chart.Search(""); Assert.Equal(2, Regex.Matches(html(), "data-tenant-bar=").Count);
            return Task.CompletedTask;
        });

    [Fact]
    public Task Metrics_sort_independently_and_click_drills_down_to_the_exact_tenant()
    {
        return Render([new(1,"First",false,100,1,2,3,null),new(2,"Second",false,1,50,60,70,null)], async (chart, html) =>
        {
            chart.Metric("Hawalas");
            Assert.True(html().IndexOf("data-tenant-bar=\"2\"", StringComparison.Ordinal) < html().IndexOf("data-tenant-bar=\"1\"", StringComparison.Ordinal));
            Assert.Contains("width:2%", html());
            chart.Metric("Changes"); Assert.Contains("تغییرات ثبت‌شده", html());
            chart.Metric("Users"); Assert.Contains("کاربران دارای عملیات", html());
            chart.Metric("invalid"); Assert.Contains("کاربران دارای عملیات", html());
            Assert.Contains("href=\"/platform/tenants/2/activity\"", html());
            await Task.CompletedTask;
        });
    }

    [Fact]
    public Task Zero_empty_long_names_and_untrusted_names_are_rendered_safely() =>
        Render([Row(1,0,"<script>alert(1)</script>"),Row(2,0,new string('ب',300))], (chart, html) =>
        {
            Assert.Contains("width:0%", html()); Assert.DoesNotContain("NaN", html()); Assert.DoesNotContain("Infinity", html());
            // Check encoded source, not HtmlDecode output, for XSS safety.
            Assert.Contains("0%", html());
            chart.Search("absent"); Assert.Contains("صرافی‌ای با این جستجو یافت نشد", html());
            return Task.CompletedTask;
        });

    [Fact]
    public Task Empty_dataset_has_an_explicit_message() => Render([], (_, html) =>
    {
        Assert.Contains("صرافی‌ای برای نمایش وجود ندارد", html()); return Task.CompletedTask;
    });

    [Fact]
    public Task Page_size_is_bounded_and_large_numbers_use_english_digits() =>
        Render(Enumerable.Range(1, 1000).Select(i => Row(i, 100000 + i)).ToArray(), (chart, html) =>
        {
            chart.Size("15"); Assert.Equal(15, Regex.Matches(html(), "data-tenant-bar=").Count);
            Assert.Contains("101,000", html());
            chart.Size("1000"); Assert.Equal(15, Regex.Matches(html(), "data-tenant-bar=").Count);
            chart.Size("10"); Assert.Equal(10, Regex.Matches(html(), "data-tenant-bar=").Count);
            return Task.CompletedTask;
        });

    public sealed class Probe : TenantActivityChart
    {
        [Parameter] public Action<Probe> Capture { get; set; } = default!;
        protected override void OnInitialized() => Capture(this);
        public void Search(string value) { SearchChanged(new ChangeEventArgs { Value = value }); StateHasChanged(); }
        public void Metric(string value) { MetricChanged(new ChangeEventArgs { Value = value }); StateHasChanged(); }
        public void Size(string value) { PageSizeChanged(new ChangeEventArgs { Value = value }); StateHasChanged(); }
        public void Page(int value) { ChangePage(value); StateHasChanged(); }
    }
}
