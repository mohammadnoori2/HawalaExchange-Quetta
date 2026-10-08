using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class PageRefreshTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Refresh_reads_new_database_values_and_invalidates_cache_without_saving_pending_edits()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var account = new Account { AccountCode = $"REFRESH-{Guid.NewGuid():N}", AccountName = "Old", AccountType = "Cash" };
        var pending = new Account { AccountCode = $"PENDING-{Guid.NewGuid():N}", AccountName = "Pending", AccountType = "Cash" };
        context.Accounts.AddRange(account, pending); await context.SaveChangesAsync();
        await using (var other = fixture.CreateContext())
        {
            using var otherBypass = other.BypassSubscriptionEnforcement();
            await other.Accounts.Where(x => x.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountName, "New from database"));
        }
        pending.AccountName = "Unsaved EF edit";
        var calls = 0;
        var currencies = new Mock<ICurrencyService>();
        currencies.Setup(x => x.GetActiveCurrenciesAsync()).ReturnsAsync(() => { calls++; return new List<CurrencyDto> { new() { Code = "USD" } }; });
        var tenant = new TestCurrentTenant { TenantId = 0 };
        var cache = new UiReferenceDataCache(tenant, currencies.Object, Mock.Of<IPaymentLocationService>(), Mock.Of<ICompanySettingService>());
        await cache.GetActiveCurrenciesAsync();
        var dates = new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, tenant);
        using var services = new ServiceCollection().AddLogging().AddSingleton(context).AddSingleton(cache).AddSingleton(dates).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string? refreshedName = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            // HtmlRenderer cannot click; invoke the actual event handler through a captured component.
            var component = await renderer.RenderComponentAsync<RefreshHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(RefreshHost.Callback)] = EventCallback.Factory.Create(this, async () => { refreshedName = (await fixture.CreateAccountService(context).GetByIdAsync(account.Id))!.AccountName; await cache.GetActiveCurrenciesAsync(); }) }));
            await (Task)typeof(PageRefreshButton).GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(RefreshHost.Button, null)!;
        });
        Assert.Equal("New from database", refreshedName); Assert.Equal(2, calls);
        Assert.Equal(EntityState.Modified, context.Entry(pending).State);
        await using var verify = fixture.CreateContext();
        Assert.Equal("Pending", (await verify.Accounts.AsNoTracking().SingleAsync(x => x.Id == pending.Id)).AccountName);
    }

    public sealed class RefreshHost : ComponentBase
    {
        [Parameter] public EventCallback Callback { get; set; }
        public static PageRefreshButton Button { get; private set; } = default!;
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<PageRefreshButton>(0); builder.AddAttribute(1, "Refresh", Callback);
            builder.AddComponentReferenceCapture(2, value => Button = (PageRefreshButton)value); builder.CloseComponent();
        }
    }

    [Fact]
    public void Every_interactive_database_page_has_an_explicit_refresh_callback()
    {
        var root = SourceRoot();
        var files = Directory.GetFiles(Path.Combine(root, "HawalaExchange.Web", "Components", "Pages"), "*.razor", SearchOption.AllDirectories);
        var pages = files.Where(path => Path.GetFileName(path) != "Counter.razor").Select(File.ReadAllText)
            .Where(text => text.StartsWith("@page ") && text.Contains("@rendermode InteractiveServer")).ToList();
        Assert.True(pages.Count >= 48);
        Assert.All(pages, text => { Assert.Contains("<PageRefreshButton Refresh=\"RefreshPageDataAsync\"", text); Assert.Contains("private async Task RefreshPageDataAsync()", text); });
    }
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Directory.GetParent(Path.GetDirectoryName(source)!)!.FullName;
}
