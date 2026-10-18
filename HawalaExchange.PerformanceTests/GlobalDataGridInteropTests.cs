using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace HawalaExchange.PerformanceTests;

public sealed class GlobalDataGridInteropTests
{
    private static ServiceProvider Services(Browser browser, bool shortTimeout=false) => new ServiceCollection().AddLogging().AddOptions()
        .Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options=>
            options.JSInteropDefaultCallTimeout=shortTimeout?TimeSpan.FromMilliseconds(20):TimeSpan.FromSeconds(30))
        .AddSingleton<IJSRuntime>(browser).AddSingleton<UiConnectionState>().BuildServiceProvider();

    private static async Task<GridProbe> Render(HtmlRenderer renderer, bool sorting=true)
    {
        GridProbe? grid=null;
        await renderer.RenderComponentAsync<GridProbe>(ParameterView.FromDictionary(new Dictionary<string,object?>
        {
            [nameof(GridProbe.Capture)]=(Action<GridProbe>)(value=>grid=value),
            [nameof(GlobalDataGrid.EnableSorting)]=sorting,
            [nameof(GlobalDataGrid.ChildContent)]=(RenderFragment)(b=>b.AddMarkupContent(0,"<tbody><tr><td>Record</td></tr></tbody>"))
        }));
        return grid!;
    }

    [Theory]
    [InlineData("import",false)] [InlineData("import",true)]
    [InlineData("initialize",false)] [InlineData("initialize",true)]
    public async Task Cancellation_and_disconnect_are_nonfatal_and_retry_on_next_render(string stage,bool disconnected)
    {
        var browser=new Browser();
        browser.Handle=(name,_)=>name==stage?Task.FromException<object?>(disconnected?new JSDisconnectedException("Disconnected"):new TaskCanceledException()):Task.FromResult<object?>(name=="import"?browser:null);
        using var services=Services(browser);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var grid=await Render(renderer);
            await grid.AfterRender(true);
            Assert.Equal(stage=="import"?1:2,browser.Calls.Count); // No immediate retry loop.
            browser.Handle=(name,_)=>Task.FromResult<object?>(name=="import"?browser:null);
            await grid.AfterRender(false);
            Assert.Equal("initialize",browser.Calls.Last());
            Assert.Equal(2,browser.Calls.Count(x=>x==stage));
        });
    }

    [Fact]
    public async Task Pending_import_is_canceled_on_disconnect_and_can_initialize_after_reconnect()
    {
        var browser=new Browser{Handle=async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return null;}};
        using var services=Services(browser);var connection=services.GetRequiredService<UiConnectionState>();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var grid=await Render(renderer);var pending=grid.AfterRender(true);
            await grid.AfterRender(false); // Concurrent render must not start another import.
            await connection.OnConnectionDownAsync(null!,CancellationToken.None);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await grid.AfterRender(false);Assert.Single(browser.Calls);
            browser.Handle=(name,_)=>Task.FromResult<object?>(name=="import"?browser:null);
            await connection.OnConnectionUpAsync(null!,CancellationToken.None);
            await grid.AfterRender(false);Assert.Equal("initialize",browser.Calls.Last());
        });
    }

    [Fact]
    public async Task Disposal_cancels_pending_import_and_prevents_later_calls()
    {
        var browser=new Browser{Handle=async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return null;}};
        using var services=Services(browser);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var grid=await Render(renderer);var pending=grid.AfterRender(true);
            await grid.DisposeAsync();await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await grid.DisposeAsync();await grid.AfterRender(false);Assert.Single(browser.Calls);
        });
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Cleanup_cancellation_or_disconnect_does_not_escape_disposal(bool disconnected)
    {
        var browser=new Browser();using var services=Services(browser);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var grid=await Render(renderer);await grid.AfterRender(true);
            browser.DisposeFailure=disconnected?new JSDisconnectedException("Disconnected"):new TaskCanceledException();
            await grid.DisposeAsync();await grid.DisposeAsync();Assert.Equal(1,browser.Disposals);
        });
    }

    [Fact]
    public async Task A_late_import_after_disposal_is_released_without_initializing()
    {
        var completion=new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser=new Browser{Handle=(_,_)=>completion.Task};
        using var services=Services(browser);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var grid=await Render(renderer);var pending=grid.AfterRender(true);
            await grid.DisposeAsync();completion.SetResult(browser);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(browser.Calls);Assert.Equal("import",browser.Calls[0]);Assert.Equal(1,browser.Disposals);
        });
    }

    [Fact]
    public async Task An_unresponsive_browser_retains_the_configured_timeout()
    {
        var browser=new Browser{Handle=async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return null;}};
        using var services=Services(browser,true);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var grid=await Render(renderer);await grid.AfterRender(true).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(browser.Calls);
        });
    }

    [Fact]
    public async Task Real_script_errors_remain_observable_and_disabled_sorting_does_not_call_js()
    {
        var browser=new Browser{Handle=(_,_)=>Task.FromException<object?>(new JSException("Broken script"))};
        using var services=Services(browser);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var disabled=await Render(renderer,false);await disabled.AfterRender(true);Assert.Empty(browser.Calls);
            var grid=await Render(renderer);await Assert.ThrowsAsync<JSException>(()=>grid.AfterRender(true));
        });
    }

    public sealed class GridProbe:GlobalDataGrid
    {
        [Parameter] public Action<GridProbe> Capture {get;set;}=default!;
        protected override void OnInitialized(){base.OnInitialized();Capture(this);}
        public Task AfterRender(bool first)=>base.OnAfterRenderAsync(first);
    }
    private sealed class Browser:IJSRuntime,IJSObjectReference
    {
        public List<string> Calls {get;}=[];
        public int Disposals {get;private set;}
        public Exception? DisposeFailure {get;set;}
        public Func<string,CancellationToken,Task<object?>>? Handle {get;set;}
        public ValueTask<T> InvokeAsync<T>(string name,object?[]? args)=>InvokeAsync<T>(name,CancellationToken.None,args);
        public async ValueTask<T> InvokeAsync<T>(string name,CancellationToken token,object?[]? args)
        {
            Calls.Add(name);var result=Handle is null?(name=="import"?this:null):await Handle(name,token);
            return result is null?default!:(T)result;
        }
        public ValueTask DisposeAsync(){Disposals++;return DisposeFailure is null?ValueTask.CompletedTask:ValueTask.FromException(DisposeFailure);}
    }
}
