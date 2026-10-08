using System.Net;
using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.PerformanceTests;

public sealed class RecordFilterLayoutTests
{
    private static ServiceProvider Services() => new ServiceCollection().AddLogging()
        .AddSingleton(Moq.Mock.Of<Microsoft.JSInterop.IJSRuntime>())
        .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 0 }))
        .BuildServiceProvider();

    [Fact]
    public async Task Empty_date_picker_has_no_helper_rows_below_the_control()
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<PersianDatePicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(PersianDatePicker.Value)] = DateTime.MinValue, [nameof(PersianDatePicker.AllowEmpty)] = true }));
            var empty = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.DoesNotContain("calendar-date-equivalents", empty);
            Assert.DoesNotContain("0001", empty);
        });
    }

    [Fact]
    public async Task Unified_date_picker_keeps_calendar_clear_and_disabled_behaviors()
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        DateHost? host = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<DateHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(DateHost.Capture)] = (Action<DateHost>)(h => host = h) }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("date-picker-input", html);
            Assert.Contains("date-picker-calendar", html);
            Assert.Contains("date-picker-clear", html);
            Assert.DoesNotContain("class=\"input-group\"", html);
            Assert.DoesNotContain("btn-outline-secondary", html);
            Assert.Contains("1405/07/16", html);
            Assert.DoesNotContain("calendar-date-equivalents", html);
            Assert.Contains("title=\"شمسی:", html);
            Assert.Contains(HawalaExchange.Web.Helpers.DualCalendarDateHelper.GregorianText(host!.Value, false), html);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            await EventCallback.Factory.Create(host!.Picker, () =>
            {
                typeof(PersianDatePicker).GetMethod("OpenCalendar", flags)!.Invoke(host.Picker, null);
            }).InvokeAsync();
            Assert.Contains("role=\"dialog\"", root.ToHtmlString());
            Assert.Contains("calendar-date-equivalents", root.ToHtmlString());
            Assert.Contains("شمسی:", WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.Contains("میلادی:", WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.Contains("نوع تقویم", WebUtility.HtmlDecode(root.ToHtmlString()));
            await (Task)typeof(PersianDatePicker).GetMethod("ClearDate", flags)!.Invoke(host.Picker, null)!;
            Assert.Equal(DateTime.MinValue, host.Value);
            Assert.DoesNotContain("calendar-date-equivalents", root.ToHtmlString());
            Assert.DoesNotContain("date-picker-clear", root.ToHtmlString());
            Assert.DoesNotContain("role=\"dialog\"", root.ToHtmlString());
            host.Disabled = true;
            host.Refresh();
            await EventCallback.Factory.Create(host.Picker, () =>
            {
                typeof(PersianDatePicker).GetMethod("OpenCalendar", flags)!.Invoke(host.Picker, null);
            }).InvokeAsync();
            Assert.DoesNotContain("role=\"dialog\"", root.ToHtmlString());
            Assert.Contains("disabled", root.ToHtmlString());
        });
    }

    [Fact]
    public async Task Customers_show_fixed_labeled_fields_inside_the_hawala_filter_bar_and_reset_together()
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Host? host = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(Host.Capture)] = (Action<Host>)(h => host = h) }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("compact-filter-bar", html);
            Assert.Contains("compact-filter-search", html);
            Assert.Contains("compact-advanced-filters", html);
            Assert.Contains("فیلترهای بیشتر", html);
            foreach (var label in new[] { "کد مشتری", "نام کامل", "نام پدر", "شماره تماس", "نمبر تذکره", "آدرس", "یادداشت" })
                Assert.DoesNotContain(label, html);
            Assert.Contains("از تاریخ ثبت", html);
            Assert.Contains("تا تاریخ ثبت", html);
            Assert.DoesNotContain("افزودن شرط", html);
            Assert.DoesNotContain("<details", html);
            Assert.DoesNotContain("PhotoPath", html);
            Assert.DoesNotContain("بایگانی", html); // Already handled by the page's archive toggle.

            host!.Filters.Search = "Ali";
            host.Filters.For("CreatedAt").From = DateTime.Today;
            host.Refresh();
            Assert.Single(host.Filters.Apply(host.Items));
            Assert.Contains("compact-reset-filter", root.ToHtmlString());
            await (Task)typeof(CompactFilterBar).GetMethod("ResetAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Bar, null)!;
            host.Refresh();
            Assert.Equal(2, host.Filters.Apply(host.Items).Count());
            Assert.Equal(0, host.Filters.Count);
            Assert.Empty(host.Filters.Search);
            Assert.Contains("data-expanded=\"false\"", root.ToHtmlString());
            Assert.DoesNotContain("compact-reset-filter", root.ToHtmlString());
            typeof(CompactFilterBar).GetMethod("ToggleAdvancedFilters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Bar, null);
            host.Refresh();
            Assert.Contains("compact-advanced-filters", root.ToHtmlString());
            Assert.False(host.Filters.For("CreatedAt").Active);
        });
    }

    [Fact]
    public async Task Correspondents_use_the_same_search_quick_selects_and_more_filters_control()
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var state = new RecordFilterSet();
            var items = new[] { new CorrespondentDto { Name = "Quetta", Country = "Pakistan", City = "Quetta", SettlementCurrencyCode = "USD" } };
            var root = await renderer.RenderComponentAsync<RecordFilterBar<CorrespondentDto>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["State"] = state, ["Items"] = items, ["Exclude"] = "IsArchived,Code,Name,PhoneNumber,Address,Remarks" }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("compact-filter-bar", html);
            Assert.Contains("compact-filter-search", html);
            Assert.Equal(2, html.Split("class=\"form-select compact-quick-filter\"").Length - 1);
            Assert.Contains("Pakistan", html);
            Assert.Contains("Quetta", html);
            Assert.Contains("فیلترهای بیشتر", html);
            Assert.DoesNotContain("<details", html);
            Assert.Contains("data-expanded=\"false\"", html); // Overflow is collapsed; fitting fields stay visible.
            Assert.Equal(0, state.Count); // Creating empty fixed controls must not activate filters.
            Assert.DoesNotContain(state.Rules, x => x.Field is "Code" or "Name" or "PhoneNumber" or "Address" or "Remarks");
        });
    }

    [Fact]
    public async Task Account_fields_match_the_same_labeled_grid_without_duplicating_the_existing_type_filter()
    {
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var state = new RecordFilterSet();
            var root = await renderer.RenderComponentAsync<RecordFilterFields<AccountDto>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["State"] = state, ["Items"] = new[] { new AccountDto { AccountName = "Customer account", AccountCode = "C-1", AccountType = "Customer", ReferenceName = "Ali" } },
                ["Exclude"] = "AccountType,IsArchived,AccountCode,ReferenceName", ["QuickLimit"] = 0
            }));
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("compact-filter-field", html);
            Assert.DoesNotContain("کد حساب", html);
            Assert.DoesNotContain("صاحب حساب", html);
            Assert.Contains("فعال", html);
            Assert.Contains("از تاریخ ثبت", html);
            Assert.DoesNotContain("نوع حساب", html);
            Assert.DoesNotContain("<details", html);
            Assert.DoesNotContain("افزودن شرط", html);
            Assert.DoesNotContain(state.Rules, x => x.Field is "AccountCode" or "ReferenceName");
        });
    }

    [Fact]
    public void Unified_correspondent_search_matches_details_and_only_the_linked_account_codes()
    {
        var correspondent = new CorrespondentDto { Code = "COR-2172", Name = "Quetta", PhoneNumber = "0791234567", Address = "کویته بازار", Remarks = "تصفیه ماهانه", Country = "Pakistan", City = "Balochistan" };
        foreach (var term in new[] { "2172", " QUETTA ", "079123", "بازار", "ماهانه", "Pakistan", "Balochistan", "acct-44559" })
            Assert.True(CorrespondentSearch.Matches(correspondent, term, ["ACCT-44559", "ACCT-55660"]));
        Assert.False(CorrespondentSearch.Matches(correspondent, "ACCT-44559"));
        Assert.False(CorrespondentSearch.Matches(correspondent, "ACCT-99999", ["ACCT-44559"]));
        Assert.False(CorrespondentSearch.Matches(new CorrespondentDto(), "unknown"));
        Assert.True(CorrespondentSearch.Matches(new CorrespondentDto(), null));
    }

    [Fact]
    public void Unified_customer_search_covers_all_seven_fields_and_handles_nulls()
    {
        var customer = new CustomerDto { CustomerCode = "C-2172", FullName = "Abdollah Nazari", FatherName = "Rahim", PhoneNumber = "0701234567", TazkiraNumber = "A44559", Address = "کابل برچی", Remarks = "تماس پیش از پرداخت" };
        foreach (var term in new[] { "2172", " abdollah ", "RAHIM", "070123", "44559", "برچی", "پیش از پرداخت" })
            Assert.True(CustomerSearch.Matches(customer, term));
        Assert.False(CustomerSearch.Matches(customer, "unrelated"));
        Assert.False(CustomerSearch.Matches(new CustomerDto(), "missing"));
        Assert.True(CustomerSearch.Matches(new CustomerDto(), "  "));
        Assert.True(CustomerSearch.Matches(new CustomerDto(), null));
    }

    [Fact]
    public void Unified_account_search_matches_code_name_owner_and_existing_references()
    {
        var account = new AccountDto { AccountCode = "C-2172", AccountName = "Customer account", ReferenceName = "Abdollah Nazari", ReferenceType = "Customer", ReferenceId = 44559 };
        foreach (var term in new[] { "2172", " ACCOUNT ", "abdollah", "Nazari", "Customer", "44559", "" })
            Assert.True(AccountSearch.Matches(account, term));
        Assert.False(AccountSearch.Matches(account, "unrelated"));
        Assert.False(AccountSearch.Matches(new AccountDto(), "someone"));
        Assert.True(AccountSearch.Matches(new AccountDto(), null));
        account.ReferenceName = "کابل برچی";
        Assert.True(AccountSearch.Matches(account, "برچی"));
    }

    [Fact]
    public void Fixed_field_state_is_unique_and_clear_resets_search_dates_amounts_and_choices()
    {
        var state = new RecordFilterSet { Search = "2172" };
        var amount = state.For("Amount");
        amount.Min = 10; amount.Max = 20;
        state.For("CreatedAt").From = DateTime.Today;
        state.For("CurrencyCode", exact: true).Text = "USD";
        Assert.Same(amount, state.For("Amount"));
        Assert.Equal(3, state.Count);
        Assert.Equal(3, state.Rules.Count);
        var rows = new[] { new CorrespondentPeriodDto { PeriodNumber = 2172 }, new CorrespondentPeriodDto { PeriodNumber = 2 } };
        var search = new RecordFilterSet { Search = "2172" };
        Assert.Equal(2172, Assert.Single(search.Apply(rows)).PeriodNumber);
        state.Clear();
        Assert.Empty(state.Search);
        Assert.Empty(state.Rules);
        Assert.False(state.For("Amount").Active);
    }

    public sealed class DateHost : ComponentBase
    {
        [Parameter] public Action<DateHost> Capture { get; set; } = default!;
        public DateTime Value { get; private set; } = new(2026, 10, 8);
        public bool Disabled { get; set; }
        public PersianDatePicker Picker { get; private set; } = default!;
        public void Refresh() => StateHasChanged();
        protected override void OnInitialized() => Capture(this);
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<PersianDatePicker>(0);
            b.AddAttribute(1, "Value", Value);
            b.AddAttribute(2, "ValueChanged", EventCallback.Factory.Create<DateTime>(this, value => { Value = value; Refresh(); }));
            b.AddAttribute(3, "AllowEmpty", true);
            b.AddAttribute(4, "Disabled", Disabled);
            b.AddComponentReferenceCapture(5, component => Picker = (PersianDatePicker)component);
            b.CloseComponent();
        }
    }

    public sealed class Host : ComponentBase
    {
        [Parameter] public Action<Host> Capture { get; set; } = default!;
        public RecordFilterSet Filters { get; } = new();
        public CustomerDto[] Items { get; } = [new() { FullName = "Ali", PhoneNumber = "070111", CreatedAt = DateTime.Today }, new() { FullName = "Omar", PhoneNumber = "079222", CreatedAt = DateTime.Today.AddDays(-1) }];
        public CompactFilterBar Bar { get; private set; } = default!;
        public void Refresh() => StateHasChanged();
        protected override void OnInitialized() => Capture(this);
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<CompactFilterBar>(0);
            b.AddAttribute(1, "InitiallyExpanded", true);
            b.AddAttribute(2, "HasActiveFilters", Filters.Count > 0 || Filters.Search.Length > 0);
            b.AddAttribute(3, "ActiveFilterCount", Filters.Count);
            b.AddAttribute(4, "Reset", EventCallback.Factory.Create(this, () => { Filters.Clear(); Refresh(); }));
            b.AddAttribute(5, "SearchContent", (RenderFragment)(child => { child.OpenElement(0, "input"); child.AddAttribute(1, "class", "form-control"); child.CloseElement(); }));
            b.AddAttribute(6, "AdvancedFilters", (RenderFragment)(child =>
            {
                child.OpenComponent<RecordFilterFields<CustomerDto>>(0);
                child.AddAttribute(1, "State", Filters);
                child.AddAttribute(2, "Items", Items);
                child.AddAttribute(3, "Exclude", "CustomerType,IsArchived,CustomerCode,FullName,FatherName,PhoneNumber,TazkiraNumber,Address,Remarks");
                child.CloseComponent();
            }));
            b.AddComponentReferenceCapture(7, component => Bar = (CompactFilterBar)component);
            b.CloseComponent();
        }
    }
}
