using System.Security.Claims;
using System.Net;
using System.Reflection;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Services;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class PlatformActivityTests(SqlServerPerformanceFixture fixture)
{
    private static readonly DateTime Start = new(2026, 8, 10);

    private PlatformActivityService Service(ClaimsPrincipal principal)
    {
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(fixture.CreateContext()));
        var auth = new Mock<AuthenticationStateProvider>();
        auth.Setup(x => x.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(principal));
        return new PlatformActivityService(factory.Object, auth.Object, Options.Create(new IdentityOptions()));
    }

    private static ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim("platform_user", "true"),
        new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp!)
    ], "Test"));

    private async Task<(ApplicationUser Actor, long First, long Second, long Empty)> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        using var bypass = db.BypassSubscriptionEnforcement();
        var tenants = Enumerable.Range(1, 3).Select(i => new Tenant { Name = $"Activity {i} {Guid.NewGuid():N}" }).ToArray();
        db.Tenants.AddRange(tenants);
        await db.SaveChangesAsync();
        var actor = new ApplicationUser { TenantId = 1, IsPlatformUser = true, IsActive = true,
            UserName = Guid.NewGuid().ToString("N"), LocalUserName = Guid.NewGuid().ToString("N"),
            FullName = "Activity viewer", SecurityStamp = Guid.NewGuid().ToString("N") };
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Name == PlatformRoles.Viewer);
        if (role is null)
        {
            role = new IdentityRole<long>(PlatformRoles.Viewer) { NormalizedName = PlatformRoles.Viewer.ToUpperInvariant() };
            db.Roles.Add(role); await db.SaveChangesAsync();
        }
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = actor.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        foreach (var tenant in tenants.Take(2))
        {
            using var scope = db.UseTenantScope(tenant.Id);
            var branch = new Branch { Name = "Activity branch", Code = "ACT" };
            db.Branches.Add(branch); await db.SaveChangesAsync();
            var user = new ApplicationUser { TenantId = tenant.Id, BranchId = branch.Id, FullName = "Worker",
                UserName = Guid.NewGuid().ToString("N"), LocalUserName = Guid.NewGuid().ToString("N") };
            var platformUser = new ApplicationUser { TenantId = tenant.Id, IsPlatformUser = true, FullName = "Platform actor",
                UserName = Guid.NewGuid().ToString("N"), LocalUserName = Guid.NewGuid().ToString("N") };
            db.Users.AddRange(user, platformUser); await db.SaveChangesAsync();
            foreach (var (date, type) in new[] { (Start.AddTicks(-1), "Exchange"), (Start, "HawalaReceive"),
                (Start.AddHours(23), "Exchange"), (Start.AddDays(1), "HawalaSend"), (Start.AddDays(2), "Exchange") })
                db.Transactions.Add(new Transaction { TenantId = tenant.Id, BranchId = branch.Id, CreatedBy = user.Id,
                    CreatedAt = date, TransactionNo = Guid.NewGuid().ToString("N"), TransactionType = type, Status = "Paid" });
            await db.SaveChangesAsync();
            // Remove only synthetic setup audits, in the disposable test database.
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenant.Id).ExecuteDeleteAsync();
            db.AuditLogs.AddRange(
                new AuditLog { TenantId = tenant.Id, UserId = user.Id, CreatedAt = Start, Action = "CREATE", TableName = "Transactions", RecordId = 1, ProcessId = Guid.NewGuid() },
                new AuditLog { TenantId = tenant.Id, UserId = user.Id, CreatedAt = Start.AddDays(1), Action = "UPDATE", TableName = "Transactions", RecordId = 1, ProcessId = Guid.NewGuid() },
                new AuditLog { TenantId = tenant.Id, UserId = platformUser.Id, CreatedAt = Start, Action = "UPDATE", TableName = "Tenant", RecordId = 1, ProcessId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        return (actor, tenants[0].Id, tenants[1].Id, tenants[2].Id);
    }

    [Fact]
    public async Task Financial_breakdown_includes_real_cash_exchange_expense_and_capital_tables_without_audit_double_counting()
    {
        var seed = await SeedAsync();
        await using var db = fixture.CreateContext();
        using var scope = db.UseTenantScope(seed.First);
        using var bypass = db.BypassSubscriptionEnforcement();
        var worker = await db.Users.IgnoreQueryFilters().FirstAsync(x => x.TenantId == seed.First && !x.IsPlatformUser);
        var currency = new Currency { Code = "TST", Name = "Test currency" };
        var otherCurrency = new Currency { Code = "TS2", Name = "Other test currency" };
        var account = new Account { AccountCode = "ACT-CASH", AccountName = "Cash", AccountType = "Cash" };
        var other = new Account { AccountCode = "ACT-OTHER", AccountName = "Other", AccountType = "Equity" };
        db.AddRange(currency, otherCurrency, account, other); await db.SaveChangesAsync();
        db.AccountMoneyOperations.AddRange(
            new AccountMoneyOperation { OperationType = "Deposit", CreatedAt = Start, OperationDate = Start, AccountId = other.Id, CashOrBankAccountId = account.Id, CurrencyId = currency.Id, Amount = 100, CreatedBy = worker.Id },
            new AccountMoneyOperation { OperationType = "Withdraw", CreatedAt = Start.AddDays(1), OperationDate = Start.AddDays(1), AccountId = other.Id, CashOrBankAccountId = account.Id, CurrencyId = currency.Id, Amount = 50, CreatedBy = worker.Id },
            new AccountMoneyOperation { OperationType = "Deposit", CreatedAt = Start, OperationDate = Start, AccountId = other.Id, CashOrBankAccountId = account.Id, CurrencyId = currency.Id, Amount = 100, CreatedBy = worker.Id, IsDeleted = true });
        db.MoneyExchangeOperations.Add(new MoneyExchangeOperation { ExchangeDate = Start, CreatedAt = Start, FromAccountId = account.Id, ToAccountId = other.Id, FromCurrencyId = currency.Id, ToCurrencyId = otherCurrency.Id, FromAmount = 100, ToAmount = 10, ExchangeRate = 10, CreatedBy = worker.Id });
        db.Expenses.Add(new Expense { Title = "Expense", ExpenseDate = Start, CreatedAt = Start, CurrencyId = currency.Id, ExpenseAccountId = other.Id, PaidFromAccountId = account.Id, Amount = 10, CreatedBy = worker.Id });
        db.CapitalInvestments.Add(new CapitalInvestment { InvestmentDate = Start, CreatedAt = Start, CurrencyId = currency.Id, ReceivingAccountId = account.Id, CapitalAccountId = other.Id, Amount = 100, CreatedBy = worker.Id });
        await db.SaveChangesAsync();
        var report = await Service(Principal(seed.Actor)).GetAsync(Start, Start.AddDays(1), seed.First);
        Assert.Equal(8, report.Transactions); // 3 transactions + 2 cash + 1 exchange + 1 expense + 1 capital.
        Assert.Equal(8, report.Types.Sum(x => x.Count));
        Assert.Equal(1, report.Types.Single(x => x.Code == "Deposit").Count);
        Assert.Equal(1, report.Types.Single(x => x.Code == "Withdraw").Count);
        Assert.Equal(2, report.Types.Single(x => x.Code == "Exchange").Count); // Includes legacy transaction exchange.
        Assert.Equal(1, report.Types.Single(x => x.Code == "HawalaReceive").Count);
        Assert.Equal(1, report.Types.Single(x => x.Code == "HawalaSend").Count);
        Assert.Equal(8, report.Days.Sum(x => x.Types.Sum(t => t.Count)));
        Assert.Equal(1, report.ActiveUsers);
        var foreign = await Service(Principal(seed.Actor)).GetAsync(Start, Start.AddDays(1), seed.Second);
        Assert.Equal(3, foreign.Transactions); Assert.Equal(0, foreign.Types.Single(x => x.Code == "Deposit").Count);
    }

    [Fact]
    public async Task Selected_tenant_counts_are_date_bounded_daily_distinct_and_read_only()
    {
        var seed = await SeedAsync();
        await using var db = fixture.CreateContext();
        var before = await db.AuditLogs.IgnoreQueryFilters().CountAsync();
        fixture.Commands.Reset();
        var report = await Service(Principal(seed.Actor)).GetAsync(Start, Start.AddDays(1), seed.First);
        Assert.InRange(fixture.Commands.Count, 1, 9);
        var row = Assert.Single(report.Tenants);
        Assert.Equal(seed.First, row.TenantId);
        Assert.Equal(3, row.Transactions);
        Assert.Equal(2, row.Hawalas);
        Assert.Equal(3, row.Changes);
        Assert.Equal(1, row.ActiveUsers); // Same user on two days, platform actor excluded.
        Assert.Equal(Start.AddDays(1), row.LastOperationAt);
        Assert.Equal(3, report.Transactions);
        Assert.Equal(2, report.Hawalas); Assert.Equal(3, report.Changes);
        Assert.Equal(1, report.ActiveTenants);
        Assert.Equal(1, report.ActiveUsers);
        Assert.Equal(new long[] { 2, 1 }, report.Days.Select(x => x.Transactions));
        Assert.Equal(new long[] { 1, 1 }, report.Days.Select(x => x.Hawalas));
        Assert.Equal(before, await db.AuditLogs.IgnoreQueryFilters().CountAsync());
        Assert.Contains(report.Choices, x => x.Id == seed.Second);
    }

    [Fact]
    public async Task Fixed_tenant_detail_panel_shows_vertical_charts_and_cannot_switch_to_another_tenant()
    {
        var seed=await SeedAsync();
        var display=new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options,new TestCurrentTenant{TenantId=0});
        using var provider=new ServiceCollection().AddLogging().AddSingleton(display)
            .AddSingleton(Service(Principal(seed.Actor))).AddSingleton(new UiConnectionState()).AddSingleton(Mock.Of<IJSRuntime>())
            .AddSingleton(Options.Create(new Microsoft.AspNetCore.Components.Server.CircuitOptions())).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            ActivityProbe? component=null;
            var root=await renderer.RenderComponentAsync<ActivityProbe>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { [nameof(ActivityProbe.TenantId)]=seed.First,[nameof(ActivityProbe.FromDate)]=Start,[nameof(ActivityProbe.ToDate)]=Start.AddDays(1),[nameof(ActivityProbe.Capture)]=(Action<ActivityProbe>)(x=>component=x)}));
            string Html()=>WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("تفکیک فعالیت مالی صرافی",Html());Assert.Contains("روند روزانه فعالیت",Html());
            Assert.DoesNotContain("id=\"platform-activity-tenant\"",Html());
            typeof(PlatformActivityPanel).GetField("selectedTenant",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(component,seed.Second);
            await component!.RefreshAsync();
            Assert.Contains($"/platform/tenants/{seed.First}/activity",Html());
            Assert.DoesNotContain($"/platform/tenants/{seed.Second}/activity",Html());
        });
    }

    [Fact]
    public async Task Rendered_panel_supports_tenant_drilldown_daily_rows_refresh_empty_and_validation_states()
    {
        var seed = await SeedAsync();
        var display = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 0 });
        using var provider = new ServiceCollection().AddLogging().AddSingleton(display)
            .AddSingleton(Service(Principal(seed.Actor))).AddSingleton(new UiConnectionState())
            .AddSingleton(Mock.Of<IJSRuntime>())
            .AddSingleton(Options.Create(new Microsoft.AspNetCore.Components.Server.CircuitOptions())).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ActivityProbe? panel = null;
            var root = await renderer.RenderComponentAsync<ActivityProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ActivityProbe.Capture)] = (Action<ActivityProbe>)(instance => panel = instance) }));
            Assert.NotNull(panel);
            var component = typeof(PlatformActivityPanel);
            void Set(string name, object value) => component.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, value);
            Task Select(long? id) => (Task)component.GetMethod("SelectTenantAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, [id])!;
            Set("fromDate", Start); Set("toDate", Start.AddDays(1));
            await Select(seed.First);
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());
            var html = Html();
            Assert.Contains("فعالیت صرافی‌ها", html); Assert.Contains("جزئیات روزانه (2 روز)", html);
            Assert.Contains("بازگشت به همه صرافی‌ها", html);
            Assert.Contains("activity-ranking-table", html);
            Assert.Contains($"/platform/tenants/{seed.First}/activity", html);
            Assert.Contains("platform-activity-from", html); Assert.Contains("platform-activity-to", html);
            Assert.Contains("UTC", html); Assert.DoesNotContain("در حال دریافت آمار", html);
            Assert.DoesNotContain("LastOperationAt", html);
            await Select(seed.Empty);
            Assert.Contains("در این بازه فعالیت ثبت‌شده‌ای وجود ندارد.", Html());
            await using (var db = fixture.CreateContext())
            using (db.UseTenantScope(seed.Empty))
            using (db.BypassSubscriptionEnforcement())
            {
                db.AuditLogs.Add(new AuditLog { TenantId = seed.Empty, CreatedAt = Start, Action = "UPDATE", TableName = "Tenant", RecordId = 1, ProcessId = Guid.NewGuid() });
                await db.SaveChangesAsync();
            }
            await panel!.RefreshAsync();
            Assert.DoesNotContain("در این بازه فعالیت ثبت‌شده‌ای وجود ندارد.", Html());
            await Select(null);
            Assert.DoesNotContain("بازگشت به همه صرافی‌ها", Html());
            Assert.Contains("tenant-chart-search", Html());
            Set("toDate", Start.AddDays(-1)); await panel.RefreshAsync();
            Assert.Contains("role=\"alert\"", Html()); Assert.Contains("بازه تاریخ معتبر نیست", Html());
        });
    }

    public sealed class ActivityProbe : PlatformActivityPanel
    {
        [Parameter] public Action<ActivityProbe> Capture { get; set; } = default!;
        protected override void OnInitialized() => Capture(this);
    }

    [Fact]
    public async Task All_tenants_matches_separate_reports_and_keeps_empty_tenants_and_days()
    {
        var seed = await SeedAsync(); var service = Service(Principal(seed.Actor));
        var all = await service.GetAsync(Start, Start.AddDays(2));
        var first = await service.GetAsync(Start, Start.AddDays(2), seed.First);
        var second = await service.GetAsync(Start, Start.AddDays(2), seed.Second);
        Assert.Equal(Assert.Single(first.Tenants), all.Tenants.Single(x => x.TenantId == seed.First));
        Assert.Equal(Assert.Single(second.Tenants), all.Tenants.Single(x => x.TenantId == seed.Second));
        Assert.Equal(0, all.Tenants.Single(x => x.TenantId == seed.Empty).Transactions);
        var empty = await service.GetAsync(Start, Start.AddDays(2), seed.Empty);
        Assert.Equal(3, empty.Days.Count); Assert.All(empty.Days, x => Assert.Equal(0, x.Transactions));
        Assert.Equal(0, empty.ActiveTenants); Assert.Null(Assert.Single(empty.Tenants).LastOperationAt);
        Assert.Equal(all.Tenants.Sum(x => x.Transactions), all.Transactions);
        Assert.Equal(all.Days.Sum(x => x.Transactions), all.Transactions);
        Assert.Equal(all.Tenants.Sum(x => x.ActiveUsers), all.ActiveUsers);
    }

    [Fact]
    public async Task Audit_only_activity_archived_tenants_and_maximum_range_are_supported()
    {
        var seed = await SeedAsync();
        await using var db = fixture.CreateContext();
        await db.Tenants.Where(x => x.Id == seed.Empty).ExecuteUpdateAsync(x => x.SetProperty(t => t.IsArchived, true));
        using (db.UseTenantScope(seed.Empty))
        using (db.BypassSubscriptionEnforcement())
        {
            db.AuditLogs.Add(new AuditLog { TenantId = seed.Empty, CreatedAt = Start, Action = "DELETE",
                TableName = "Transactions", RecordId = 10, ProcessId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var report = await Service(Principal(seed.Actor)).GetAsync(Start, Start.AddDays(365), seed.Empty);
        var row = Assert.Single(report.Tenants);
        Assert.True(row.Archived); Assert.Equal(0, row.Transactions); Assert.Equal(1, row.Changes);
        Assert.Equal(1, report.ActiveTenants); Assert.Equal(0, row.ActiveUsers);
        Assert.Equal(366, report.Days.Count); Assert.Equal(Start, row.LastOperationAt);
        Assert.Equal(1, report.Days[0].Changes); Assert.Equal(0, report.Days[^1].Changes);
        var oneDay = await Service(Principal(seed.Actor)).GetAsync(Start.AddHours(4), Start.AddHours(15), seed.First);
        Assert.Single(oneDay.Days); Assert.Equal(2, oneDay.Transactions); Assert.Equal(1, oneDay.ActiveUsers);
    }

    [Fact]
    public async Task Invalid_ranges_unknown_tenant_and_cancellation_are_rejected()
    {
        var seed = await SeedAsync(); var service = Service(Principal(seed.Actor));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Start, Start.AddDays(-1)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Start, Start.AddDays(366)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Start, DateTime.MaxValue));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Start, Start, -1));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Start, Start, long.MaxValue));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(Start, Start, seed.First, canceled.Token));
    }

    [Fact]
    public async Task Tenant_forged_role_revoked_stamp_disabled_and_impersonated_sessions_cannot_read_global_activity()
    {
        var seed = await SeedAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(new ClaimsPrincipal()).GetAsync(Start, Start));
        var principal = Principal(seed.Actor);
        var impersonated = Principal(seed.Actor); ((ClaimsIdentity)impersonated.Identity!).AddClaim(new Claim(ImpersonationIdentity.SessionClaim, "fake"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(impersonated).GetAsync(Start, Start));
        await using var db = fixture.CreateContext();
        await db.UserRoles.Where(x => x.UserId == seed.Actor.Id).ExecuteDeleteAsync();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, PlatformRoles.Owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(principal).GetAsync(Start, Start));
        var tenantUser = await db.Users.IgnoreQueryFilters().FirstAsync(x => x.TenantId == seed.First && !x.IsPlatformUser);
        var forgedTenant = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, tenantUser.Id.ToString()), new Claim("platform_user", "true"),
            new Claim(ClaimTypes.Role, PlatformRoles.Owner)], "Test"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(forgedTenant).GetAsync(Start, Start));
        var viewerRole = await db.Roles.SingleAsync(x => x.Name == PlatformRoles.Viewer);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = seed.Actor.Id, RoleId = viewerRole.Id }); await db.SaveChangesAsync();
        await db.Users.IgnoreQueryFilters().Where(x => x.Id == seed.Actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformUser, true).SetProperty(u => u.IsActive, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(principal).GetAsync(Start, Start));
        await db.Users.IgnoreQueryFilters().Where(x => x.Id == seed.Actor.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, true).SetProperty(u => u.SecurityStamp, "revoked"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(principal).GetAsync(Start, Start));
    }
}
