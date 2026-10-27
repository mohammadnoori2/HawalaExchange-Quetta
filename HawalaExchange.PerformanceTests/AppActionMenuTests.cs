using System.Reflection;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace HawalaExchange.PerformanceTests;

public sealed class AppActionMenuTests
{
    private static ServiceProvider Services(Browser browser) => new ServiceCollection().AddLogging().AddOptions()
        .AddSingleton<IJSRuntime>(browser).AddSingleton<NavigationManager>(new TestNavigation())
        .AddSingleton<UiConnectionState>().BuildServiceProvider();

    private static async Task<MenuProbe> Render(HtmlRenderer renderer)
    {
        MenuProbe? menu = null;
        await renderer.RenderComponentAsync<MenuProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(MenuProbe.Capture)] = (Action<MenuProbe>)(value => menu = value),
            [nameof(AppActionMenu.ChildContent)] = (RenderFragment)(builder => builder.AddContent(0, "Actions"))
        }));
        return menu!;
    }

    [Fact]
    public async Task Connection_state_is_per_circuit_and_renews_its_cancellation_token_on_reconnect()
    {
        using var first = new UiConnectionState();
        using var other = new UiConnectionState();
        var token = first.ConnectionToken;
        await first.OnConnectionDownAsync(null!, CancellationToken.None);
        Assert.True(token.IsCancellationRequested);
        Assert.False(other.ConnectionToken.IsCancellationRequested);
        Assert.True(other.IsConnected);
        await first.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.True(first.IsConnected);
        Assert.False(first.ConnectionToken.IsCancellationRequested);
        Assert.NotEqual(token, first.ConnectionToken);
        first.Dispose();
        Assert.True(first.ConnectionToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_or_disconnected_initialization_is_retried_on_next_render(bool disconnected)
    {
        var browser = new Browser();
        browser.Handle = (_, _) => Task.FromException<object?>(disconnected
            ? new JSDisconnectedException("Connection lost") : new TaskCanceledException());
        using var services = Services(browser);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            await menu.AfterRender(true);
            Assert.True(menu.NeedsSync);
            Assert.Single(browser.Calls); // Cancellation must not create an immediate retry loop.
            browser.Handle = (_, _) => Task.FromResult<object?>(null);
            await menu.AfterRender(false);
            Assert.False(menu.NeedsSync);
            Assert.Equal(2, browser.Calls.Count);
        });
    }

    [Fact]
    public async Task Disconnect_cancels_pending_work_and_reconnect_resynchronizes_the_same_menu()
    {
        var browser = new Browser();
        browser.Handle = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null; };
        using var services = Services(browser);
        var connection = services.GetRequiredService<UiConnectionState>();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            var pending = menu.AfterRender(true);
            Assert.False(pending.IsCompleted);
            await connection.OnConnectionDownAsync(null!, CancellationToken.None);
            await pending;
            await menu.AfterRender(false);
            Assert.Single(browser.Calls); // Do not send more interop calls while disconnected.
            browser.Handle = (_, _) => Task.FromResult<object?>(null);
            await connection.OnConnectionUpAsync(null!, CancellationToken.None);
            await menu.AfterRender(false);
            Assert.Equal(2, browser.Calls.Count);
            Assert.False(menu.NeedsSync);
        });
    }

    [Fact]
    public async Task Disposal_cancels_initialization_and_ignores_late_callbacks_and_renders()
    {
        var browser = new Browser();
        browser.Handle = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null; };
        using var services = Services(browser);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            var pending = menu.AfterRender(true);
            await menu.DisposeAsync();
            await pending;
            await menu.CloseFromJs();
            await menu.AfterRender(false);
            await menu.DisposeAsync();
            Assert.Single(browser.Calls);
        });
    }

    [Fact]
    public async Task Canceled_open_settings_save_and_close_are_nonfatal_and_failed_save_keeps_the_editor()
    {
        var browser = new Browser();
        using var services = Services(browser);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            await menu.AfterRender(true);
            await menu.Call("ToggleAsync");
            browser.Handle = (_, _) => Task.FromException<object?>(new TaskCanceledException());
            await menu.AfterRender(false);
            Assert.True(menu.NeedsPosition);
            await menu.Call("OpenSettingsAsync");
            Assert.False(menu.Configuring);
            browser.Handle = (_, _) => Task.FromResult<object?>(null);
            await menu.Call("OpenSettingsAsync");
            Assert.True(menu.Configuring);
            browser.Handle = (_, _) => Task.FromException<object?>(new TaskCanceledException());
            await menu.Call("SaveSettingsAsync");
            Assert.True(menu.Configuring);
            await menu.Call("CloseMenuAsync");
        });
    }

    [Fact]
    public async Task Server_prepares_shortcuts_and_refreshes_settings_without_reopening_the_menu()
    {
        var browser = new Browser();
        using var services = Services(browser);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            await menu.AfterRender(true);
            Assert.Equal("hawalaTools.syncOperationMenuQuickActions", Assert.Single(browser.Calls));
            await menu.Call("OpenSettingsAsync");
            await menu.AfterRender(false);
            Assert.Contains("hawalaTools.refreshOperationMenu", browser.Calls);
            Assert.DoesNotContain("hawalaTools.openOperationMenu", browser.Calls);
        });
    }

    [Fact]
    public async Task Genuine_script_errors_are_not_hidden_as_disconnects()
    {
        var browser = new Browser { Handle = (_, _) => Task.FromException<object?>(new JSException("Broken script")) };
        using var services = Services(browser);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            var error = await Assert.ThrowsAsync<JSException>(() => menu.AfterRender(true));
            Assert.Equal("Broken script", error.Message);
        });
    }

    [Fact]
    public async Task Connected_but_unresponsive_browser_keeps_the_configured_interop_timeout()
    {
        var browser = new Browser();
        browser.Handle = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null; };
        using var services = new ServiceCollection().AddLogging().AddOptions()
            .Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options => options.JSInteropDefaultCallTimeout = TimeSpan.FromMilliseconds(20))
            .AddSingleton<IJSRuntime>(browser).AddSingleton<NavigationManager>(new TestNavigation())
            .AddSingleton<UiConnectionState>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            await menu.AfterRender(true).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(menu.NeedsSync);
            Assert.Single(browser.Calls);
            browser.Handle = (_, _) => Task.FromResult<object?>(null);
            await menu.AfterRender(false);
            Assert.False(menu.NeedsSync);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_releases_the_dotnet_reference_even_when_browser_call_is_canceled(bool disconnected)
    {
        var browser = new Browser();
        using var services = Services(browser);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var menu = await Render(renderer);
            await menu.AfterRender(true);
            await menu.Call("ToggleAsync");
            await menu.AfterRender(false);
            Assert.True(menu.HasReference);
            browser.Handle = (_, _) => Task.FromException<object?>(disconnected
                ? new JSDisconnectedException("Connection lost") : new TaskCanceledException());
            await menu.DisposeAsync();
            Assert.False(menu.HasReference);
        });
    }

    public sealed class MenuProbe : AppActionMenu
    {
        [Parameter] public Action<MenuProbe> Capture { get; set; } = default!;
        protected override void OnInitialized() { base.OnInitialized(); Capture(this); }
        public Task AfterRender(bool firstRender) => base.OnAfterRenderAsync(firstRender);
        public Task Call(string method) => (Task)typeof(AppActionMenu).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, null)!;
        private object? Field(string name) => typeof(AppActionMenu).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(this);
        public bool NeedsSync => (bool)Field("refreshQuickActionsAfterRender")!;
        public bool NeedsPosition => (bool)Field("positionAfterRender")!;
        public bool Configuring => (bool)Field("isConfiguring")!;
        public bool HasReference => Field("dotNetReference") is not null;
    }

    private sealed class Browser : IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public Func<string, CancellationToken, Task<object?>> Handle { get; set; } = (_, _) => Task.FromResult<object?>(null);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            var result = await Handle(identifier, cancellationToken);
            return result is null ? default! : (TValue)result;
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://test.local/", "https://test.local/accounts");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
}
