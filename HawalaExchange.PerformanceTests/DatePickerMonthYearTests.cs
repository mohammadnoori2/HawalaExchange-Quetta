using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Helpers;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.PerformanceTests;

public sealed class DatePickerMonthYearTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Invoke(PersianDatePicker picker, string name, params object[] args)
    {
        var result = typeof(PersianDatePicker).GetMethod(name, Private)!.Invoke(picker, args);
        // Reflection bypasses Blazor's normal event-triggered render. Reproduce it here.
        if (result is not Task)
            typeof(ComponentBase).GetMethod("StateHasChanged", Private)!.Invoke(picker, null);
        return result;
    }
    private static T Read<T>(PersianDatePicker picker, string name) =>
        (T)typeof(PersianDatePicker).GetProperty(name, Private)!.GetValue(picker)!;
    private static ChangeEventArgs Change(int value) => new() { Value = value.ToString(CultureInfo.InvariantCulture) };

    private static async Task WithPicker(Func<Host, Func<string>, Task> test)
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,
                new TestCurrentTenant { TenantId = 0 })).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Host? host = null;
            var root = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(Host.Capture)] = (Action<Host>)(value => host = value) }));
            await test(host!, () => WebUtility.HtmlDecode(root.ToHtmlString()));
        });
    }

    [Fact]
    public Task Month_year_navigation_does_not_change_the_unified_date_field() => WithPicker((host, html) =>
    {
        static string Field(string markup) => Regex.Match(markup,
            "<div class=\"date-picker-control[^\"]*\">.*?</div>", RegexOptions.Singleline).Value;
        var before = Field(html());
        Assert.NotEmpty(before);
        Assert.Contains("date-picker-input", before);
        Assert.Contains("date-picker-icon date-picker-calendar", before);
        Assert.DoesNotContain("input-group", before);
        Assert.DoesNotContain("calendar-month-year", html());

        Invoke(host.Picker, "OpenCalendar");
        Assert.Contains("calendar-month-year", html());
        // Opening only changes the accessible expanded state, never field structure.
        Assert.Equal(before.Replace("aria-expanded=\"false\"", "aria-expanded=\"true\""), Field(html()));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Month_and_year_have_labels_and_bounded_English_year_options() => WithPicker((host, html) =>
    {
        Invoke(host.Picker, "OpenCalendar"); host.Refresh();
        Assert.Contains("aria-label=\"ماه تقویم\"", html());
        Assert.Contains("aria-label=\"سال تقویم\"", html());
        Assert.Contains("حمل", html());
        Assert.Contains("1405", html());
        Assert.InRange(Read<IEnumerable<int>>(host.Picker, "ViewYears").Count(), 1, 201);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Changing_month_and_year_only_navigates_until_a_day_is_chosen() => WithPicker(async (host, _) =>
    {
        var original = host.Value;
        Invoke(host.Picker, "OpenCalendar");
        Invoke(host.Picker, "OnViewYearChanged", Change(1399));
        Invoke(host.Picker, "OnViewMonthChanged", Change(12));
        host.Refresh();
        Assert.Equal(original, host.Value);
        Assert.Equal(0, host.Changes);
        var days = Read<IReadOnlyList<DualCalendarDay>>(host.Picker, "CalendarDays");
        var leapDay = days.Single(day => day.Day == 30).Date!.Value;
        await (Task)Invoke(host.Picker, "SelectDay", leapDay)!;
        Assert.Equal(new DateTime(2021, 3, 20), host.Value.Date);
        Assert.Equal(1, host.Changes);
    });

    [Fact]
    public Task Gregorian_navigation_uses_Gregorian_months_and_preserves_time() => WithPicker(async (host, html) =>
    {
        Invoke(host.Picker, "OpenCalendar"); Invoke(host.Picker, "ChangeCalendar", true);
        Invoke(host.Picker, "OnViewYearChanged", Change(2000));
        Invoke(host.Picker, "OnViewMonthChanged", Change(2)); host.Refresh();
        Assert.Contains("February", html());
        Assert.Contains("2000", html());
        var leapDay = Read<IReadOnlyList<DualCalendarDay>>(host.Picker, "CalendarDays").Single(day => day.Day == 29).Date!.Value;
        await (Task)Invoke(host.Picker, "SelectDay", leapDay)!;
        Assert.Equal(new DateTime(2000, 2, 29, 14, 30, 0), host.Value);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10000)]
    public Task Invalid_years_and_months_do_not_change_navigation_or_value(int invalid) => WithPicker((host, _) =>
    {
        Invoke(host.Picker, "OpenCalendar");
        var before = Read<DateTime>(host.Picker, "ViewFirstDay");
        Invoke(host.Picker, "OnViewYearChanged", Change(invalid));
        Invoke(host.Picker, "OnViewMonthChanged", Change(invalid));
        Assert.Equal(before, Read<DateTime>(host.Picker, "ViewFirstDay"));
        Assert.Equal(0, host.Changes);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Date_limits_still_reject_a_day_outside_the_allowed_range() => WithPicker(async (host, _) =>
    {
        host.Limited = true; host.Refresh();
        Invoke(host.Picker, "OpenCalendar"); Invoke(host.Picker, "OnViewYearChanged", Change(1404));
        Invoke(host.Picker, "OnViewMonthChanged", Change(1));
        var day = Read<IReadOnlyList<DualCalendarDay>>(host.Picker, "CalendarDays").First(d => d.Date.HasValue).Date!.Value;
        await (Task)Invoke(host.Picker, "SelectDay", day)!;
        Assert.Equal(new DateTime(2026, 10, 18, 14, 30, 0), host.Value);
        Assert.Equal(0, host.Changes);
    });

    [Fact]
    public Task Last_supported_Persian_year_clamps_month_and_keeps_navigation_safe() => WithPicker((host, _) =>
    {
        Invoke(host.Picker, "OpenCalendar"); Invoke(host.Picker, "OnViewMonthChanged", Change(12));
        var maxYear = new PersianCalendar().GetYear(DateTime.MaxValue);
        Invoke(host.Picker, "OnViewYearChanged", Change(maxYear));
        Assert.InRange(Read<IReadOnlyList<DualCalendarDay>>(host.Picker, "CalendarDays").Count, 1, 37);
        Invoke(host.Picker, "NextMonth");
        Assert.Equal(0, host.Changes);
        return Task.CompletedTask;
    });

    public sealed class Host : ComponentBase
    {
        [Parameter] public Action<Host> Capture { get; set; } = default!;
        public PersianDatePicker Picker { get; private set; } = default!;
        public DateTime Value { get; private set; } = new(2026, 10, 18, 14, 30, 0);
        public int Changes { get; private set; }
        public bool Limited { get; set; }
        public void Refresh() => StateHasChanged();
        protected override void OnInitialized() => Capture(this);
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<PersianDatePicker>(0);
            builder.AddAttribute(1, "Value", Value);
            builder.AddAttribute(2, "IncludeTime", true);
            builder.AddAttribute(3, "ValueChanged", EventCallback.Factory.Create<DateTime>(this, value => { Value = value; Changes++; }));
            builder.AddAttribute(4, "MinDate", Limited ? new DateTime(2026, 10, 15) : (DateTime?)null);
            builder.AddAttribute(5, "MaxDate", Limited ? new DateTime(2026, 10, 20) : (DateTime?)null);
            builder.AddComponentReferenceCapture(6, component => Picker = (PersianDatePicker)component);
            builder.CloseComponent();
        }
    }
}
