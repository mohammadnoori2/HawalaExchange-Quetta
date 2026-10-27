using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public sealed class PageLoadingTests
{
    [Fact]
    public async Task Accounts_are_ready_without_loading_form_options_then_create_loads_them()
    {
        using var setup = new Setup(typeof(AccountProbe));
        await setup.Render<AccountProbe>();
        setup.Accounts.Verify(x => x.GetAllAsync(), Times.Once);
        setup.Customers.Verify(x => x.GetAllAsync(), Times.Never);
        setup.Correspondents.Verify(x => x.GetAllAsync(), Times.Never);
        setup.Currencies.Verify(x => x.GetActiveCurrenciesAsync(), Times.Never);

        await setup.Invoke("OpenCreateModal");
        setup.Customers.Verify(x => x.GetAllAsync(), Times.Once);
        setup.Correspondents.Verify(x => x.GetAllAsync(), Times.Once);
        setup.Currencies.Verify(x => x.GetActiveCurrenciesAsync(), Times.Once);
        Assert.True(setup.Field<bool>("showModal"));
    }

    [Fact]
    public async Task Correspondents_keep_account_code_search_but_defer_form_currencies()
    {
        using var setup = new Setup(typeof(CorrespondentProbe));
        await setup.Render<CorrespondentProbe>();
        setup.Correspondents.Verify(x => x.GetAllAsync(), Times.Once);
        setup.Accounts.Verify(x => x.GetAllAsync(), Times.Once);
        setup.Currencies.Verify(x => x.GetActiveCurrenciesAsync(), Times.Never);

        await setup.Invoke("OpenCreateModal");
        setup.Currencies.Verify(x => x.GetActiveCurrenciesAsync(), Times.Once);
        Assert.True(setup.Field<bool>("showModal"));
    }

    [Fact]
    public async Task Account_edit_fetches_its_options_and_record_on_demand()
    {
        using var setup = new Setup(typeof(AccountProbe));
        setup.Accounts.Setup(x => x.GetByIdAsync(42)).ReturnsAsync(new AccountDto { Id = 42, AccountName = "Edit account" });
        await setup.Render<AccountProbe>();
        await setup.Invoke("OpenEditModal", 42L);
        setup.Accounts.Verify(x => x.GetByIdAsync(42), Times.Once);
        setup.Customers.Verify(x => x.GetAllAsync(), Times.Once);
        Assert.True(setup.Field<bool>("showModal"));
        Assert.False(setup.Field<bool>("isLoading"));
    }

    [Fact]
    public async Task Correspondent_edit_fetches_its_options_and_record_on_demand()
    {
        using var setup = new Setup(typeof(CorrespondentProbe));
        setup.Correspondents.Setup(x => x.GetByIdAsync(42)).ReturnsAsync(new CorrespondentDto { Id = 42, Name = "Edit correspondent" });
        await setup.Render<CorrespondentProbe>();
        await setup.Invoke("OpenEditModal", 42L);
        setup.Currencies.Verify(x => x.GetActiveCurrenciesAsync(), Times.Once);
        Assert.True(setup.Field<bool>("showModal"));
        Assert.False(setup.Field<bool>("isLoading"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_create_options_do_not_open_an_incomplete_form(bool accounts)
    {
        using var setup = new Setup(accounts ? typeof(AccountProbe) : typeof(CorrespondentProbe));
        if (accounts) setup.Customers.Setup(x => x.GetAllAsync()).ThrowsAsync(new InvalidOperationException("Unavailable"));
        else setup.Currencies.Setup(x => x.GetActiveCurrenciesAsync()).ThrowsAsync(new InvalidOperationException("Unavailable"));
        if (accounts) await setup.Render<AccountProbe>();
        else await setup.Render<CorrespondentProbe>();
        await setup.Invoke("OpenCreateModal");
        Assert.False(setup.Field<bool>("showModal"));
    }

    [Fact]
    public async Task Repeated_create_clicks_share_one_pending_options_load()
    {
        using var setup = new Setup(typeof(AccountProbe));
        await setup.Render<AccountProbe>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Customers.Setup(x => x.GetAllAsync()).Returns(async () =>
        {
            started.TrySetResult();
            await release.Task;
            return Array.Empty<CustomerDto>();
        });
        var first = setup.Invoke("OpenCreateModal");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await setup.Invoke("OpenCreateModal");
            setup.Customers.Verify(x => x.GetAllAsync(), Times.Once);
        }
        finally { release.TrySetResult(); await first; }
        Assert.True(setup.Field<bool>("showModal"));
        Assert.False(setup.Field<bool>("isLoading"));
    }

    [Fact]
    public async Task Hawala_table_does_not_wait_for_payment_options_and_payment_loads_fresh_options()
    {
        using var setup = new Setup(typeof(HawalaProbe));
        await setup.Render<HawalaProbe>();
        setup.Hawalas.Verify(x => x.GetHawalasAsync(It.IsAny<HawalaFilterDto>(), It.IsAny<CancellationToken>()), Times.Once);
        setup.Accounts.Verify(x => x.GetAllAsync(), Times.Never);
        setup.Customers.Verify(x => x.GetAllAsync(), Times.Never);

        await setup.Invoke("OpenPayModal", 1L, 100L);
        Assert.True(setup.Field<bool>("showPayModal"));
        await setup.Invoke("OpenPayModal", 1L, 100L);
        setup.Accounts.Verify(x => x.GetAllAsync(), Times.Exactly(2));
        setup.Customers.Verify(x => x.GetAllAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task Failed_payment_lookup_does_not_open_a_form_with_incomplete_data_and_can_retry()
    {
        using var setup = new Setup(typeof(HawalaProbe));
        await setup.Render<HawalaProbe>();
        setup.Accounts.SetupSequence(x => x.GetAllAsync())
            .ThrowsAsync(new InvalidOperationException("Database temporarily unavailable"))
            .ReturnsAsync(Array.Empty<AccountDto>());
        await setup.Invoke("OpenPayModal", 1L, 100L);
        Assert.False(setup.Field<bool>("showPayModal"));
        Assert.False(setup.Field<bool>("isLoading"));
        await setup.Invoke("OpenPayModal", 1L, 100L);
        Assert.True(setup.Field<bool>("showPayModal"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Account_endpoints_remain_static_and_business_routes_disable_duplicate_prerender(bool excluded)
    {
        var app = new App();
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(
            new ComponentTypeMetadata(excluded ? typeof(HawalaExchange.Web.Components.Account.Pages.Login) : typeof(AccountProbe))), "test"));
        typeof(App).GetProperty("HttpContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, context);
        var mode = typeof(App).GetProperty("PageRenderMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app);
        if (excluded) Assert.Null(mode);
        else Assert.False(Assert.IsType<InteractiveServerRenderMode>(mode).Prerender);
    }

    [Theory]
    [InlineData(typeof(HawalaPaymentProbe), "ConfirmPayHawala")]
    [InlineData(typeof(PendingPaymentProbe), "ConfirmPay")]
    [InlineData(typeof(ReceivedPaymentProbe), "ConfirmPay")]
    [InlineData(typeof(CorrespondentPaymentProbe), "ConfirmPayAsync")]
    public async Task Payment_receipt_uses_successful_result_before_form_is_cleared(Type probe, string confirm)
    {
        using var setup = new Setup(probe);
        var paid = new HawalaDto { Id = 7, Status = "Paid", ReceiverTazkiraNumber = "SAVED-ID", PaidAt = DateTime.UtcNow };
        setup.Hawalas.Setup(x => x.MarkAsPaidAsync(7, It.IsAny<PayHawalaDto>())).ReturnsAsync(paid);
        await setup.Render(probe);
        setup.Set("payHawalaId", 7L); setup.Set("selectedPaymentAccountId", 10L);
        setup.Set("showPayModal", true);
        setup.Set("payHawala", new HawalaDto { Id = 7, FromCurrencyCode = "AFN", CorrespondentName = "نمایندگی" });
        await setup.Invoke(confirm);
        var receipt = setup.Field<HawalaDto>("executedHawala");
        Assert.Same(paid, receipt);
        Assert.Equal("SAVED-ID", receipt.ReceiverTazkiraNumber);
        Assert.Equal("AFN", receipt.FromCurrencyCode);
        Assert.Equal("نمایندگی", receipt.CorrespondentName);
        Assert.False(setup.Field<bool>("showPayModal"));
        setup.Hawalas.Verify(x => x.MarkAsPaidAsync(7, It.IsAny<PayHawalaDto>()), Times.Once);
    }

    [Theory]
    [InlineData(typeof(HawalaPaymentProbe), "ConfirmPayHawala")]
    [InlineData(typeof(PendingPaymentProbe), "ConfirmPay")]
    [InlineData(typeof(ReceivedPaymentProbe), "ConfirmPay")]
    [InlineData(typeof(CorrespondentPaymentProbe), "ConfirmPayAsync")]
    public async Task Failed_payment_never_requests_an_execution_receipt(Type probe, string confirm)
    {
        using var setup = new Setup(probe);
        setup.Hawalas.Setup(x => x.MarkAsPaidAsync(7, It.IsAny<PayHawalaDto>())).ThrowsAsync(new InvalidOperationException("Rejected"));
        await setup.Render(probe);
        setup.Set("payHawalaId", 7L); setup.Set("selectedPaymentAccountId", 10L); setup.Set("showPayModal", true);
        await setup.Invoke(confirm);
        Assert.Null(setup.Field<HawalaDto?>("executedHawala"));
        Assert.True(setup.Field<bool>("showPayModal"));
    }

    [Theory]
    [InlineData(typeof(HawalaPaymentProbe), "ConfirmPayHawala")]
    [InlineData(typeof(PendingPaymentProbe), "ConfirmPay")]
    [InlineData(typeof(ReceivedPaymentProbe), "ConfirmPay")]
    [InlineData(typeof(CorrespondentPaymentProbe), "ConfirmPayAsync")]
    public async Task Repeated_execute_click_does_not_submit_the_payment_twice(Type probe, string confirm)
    {
        using var setup = new Setup(probe);
        var pending = new TaskCompletionSource<HawalaDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Hawalas.Setup(x => x.MarkAsPaidAsync(7, It.IsAny<PayHawalaDto>())).Returns(pending.Task);
        await setup.Render(probe);
        setup.Set("payHawalaId", 7L); setup.Set("selectedPaymentAccountId", 10L);
        var first = setup.Invoke(confirm);
        try
        {
            await setup.Invoke(confirm);
            setup.Hawalas.Verify(x => x.MarkAsPaidAsync(7, It.IsAny<PayHawalaDto>()), Times.Once);
        }
        finally
        {
            pending.TrySetResult(new HawalaDto { Id = 7, Status = "Paid" });
            await first;
        }
    }

    public sealed class HawalaPaymentProbe : HawalaExchange.Web.Components.Pages.HawalaTransactions.HawalaTransactions
    {
        protected override Task OnInitializedAsync() => Task.CompletedTask;
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }
    public sealed class PendingPaymentProbe : HawalaExchange.Web.Components.Pages.HawalaTransactions.PendingHawalas
    {
        protected override Task OnInitializedAsync() => Task.CompletedTask;
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }
    public sealed class ReceivedPaymentProbe : HawalaExchange.Web.Components.Pages.HawalaTransactions.HawalaReceivePage
    {
        protected override Task OnInitializedAsync() => Task.CompletedTask;
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }
    public sealed class CorrespondentPaymentProbe : HawalaExchange.Web.Components.Pages.Correspondents.CorrespondentHawalaPanel
    {
        protected override Task OnParametersSetAsync() => Task.CompletedTask;
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    // Suppress the visual tree only: these probes run the actual page lifecycle and
    // form event handlers, with no child components obscuring the service call counts.
    public sealed class AccountProbe : HawalaExchange.Web.Components.Pages.Account.Accounts
    { protected override void BuildRenderTree(RenderTreeBuilder builder) { } }
    public sealed class CorrespondentProbe : HawalaExchange.Web.Components.Pages.Correspondents.Correspondents
    { protected override void BuildRenderTree(RenderTreeBuilder builder) { } }
    public sealed class HawalaProbe : HawalaExchange.Web.Components.Pages.HawalaTransactions.HawalaTransactions
    { protected override void BuildRenderTree(RenderTreeBuilder builder) { } }

    private sealed class Setup : IDisposable
    {
        public Mock<IAccountService> Accounts { get; } = new();
        public Mock<ICustomerService> Customers { get; } = new();
        public Mock<ICorrespondentService> Correspondents { get; } = new();
        public Mock<ICurrencyService> Currencies { get; } = new();
        public Mock<IHawalaService> Hawalas { get; } = new();
        private readonly ServiceProvider services;
        private readonly LifecycleRenderer renderer;
        private object component = null!;
        private readonly Type page;

        public Setup(Type type)
        {
            page = type.BaseType!;
            Accounts.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<AccountDto>());
            Customers.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CustomerDto>());
            Correspondents.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CorrespondentDto>());
            Currencies.Setup(x => x.GetActiveCurrenciesAsync()).ReturnsAsync(Array.Empty<CurrencyDto>());
            Currencies.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CurrencyDto>());
            Hawalas.Setup(x => x.GetHawalasAsync(It.IsAny<HawalaFilterDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HawalaListResultDto { Items = [] });
            Hawalas.Setup(x => x.GetStatisticsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HawalaStatisticsDto());
            var tenant = Mock.Of<ICurrentTenant>();
            var locations = new Mock<IPaymentLocationService>();
            locations.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PaymentLocationDto>());
            var collection = new ServiceCollection().AddLogging();
            // Supply unused injections without constructing databases or browser services.
            for (var parent = type; parent is not null; parent = parent.BaseType)
                foreach (var property in parent.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                             .Where(x => x.GetCustomAttribute<InjectAttribute>() is not null && x.PropertyType.IsInterface))
                {
                    var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(property.PropertyType))!;
                    collection.AddSingleton(property.PropertyType, mock.Object);
                }
            collection.AddSingleton(Accounts.Object).AddSingleton(Customers.Object).AddSingleton(Correspondents.Object)
                .AddSingleton(Currencies.Object).AddSingleton(Hawalas.Object).AddSingleton<NavigationManager>(new TestNavigation())
                .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, tenant))
                .AddSingleton(new UiReferenceDataCache(tenant, Currencies.Object, locations.Object, Mock.Of<ICompanySettingService>()))
                .AddSingleton<IComponentActivator>(new CaptureActivator(value => component = value));
            services = collection.BuildServiceProvider();
            renderer = new LifecycleRenderer(services, services.GetRequiredService<ILoggerFactory>());
        }

        public Task Render<T>() where T : IComponent => renderer.Dispatcher.InvokeAsync(() => renderer.Render(typeof(T)));
        public Task Render(Type type) => renderer.Dispatcher.InvokeAsync(() => renderer.Render(type));
        public void Set(string name, object value) => page.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
        public Task Invoke(string name, params object[] args) => renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = page.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            if (result is Task task) await task;
        });
        public T Field<T>(string name) => (T)page.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        public void Dispose() { renderer.DisposeAsync().AsTask().GetAwaiter().GetResult(); services.Dispose(); }
    }

    private sealed class CaptureActivator(Action<object> capture) : IComponentActivator
    {
        public IComponent CreateInstance(Type type)
        { var instance = (IComponent)Activator.CreateInstance(type)!; capture(instance); return instance; }
    }
    private sealed class LifecycleRenderer(IServiceProvider services, ILoggerFactory logging) : Renderer(services, logging)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public Task Render(Type type) => RenderRootComponentAsync(AssignRootComponentId(InstantiateComponent(type)));
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
        protected override IComponent ResolveComponentForRenderMode(Type type, int? parentId,
            IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/hawala-transactions");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
