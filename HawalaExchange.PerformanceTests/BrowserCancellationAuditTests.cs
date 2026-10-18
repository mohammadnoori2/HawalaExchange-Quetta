using System.Reflection;
using System.Text.RegularExpressions;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Helpers;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace HawalaExchange.PerformanceTests;

public sealed class BrowserCancellationAuditTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Ui_cancellation_is_not_success_and_confirmation_fails_closed(bool disconnected)
    {
        var browser=new Browser {Failure=disconnected?new JSDisconnectedException("Disconnected"):new TaskCanceledException()};
        Assert.False(await browser.TryInvokeUiVoidAsync("window.print"));
        Assert.False(await browser.TryInvokeUiBooleanAsync("hawalaTools.copyToClipboard","text"));
        Assert.False(await browser.ConfirmOrCancelAsync("Delete?"));
        Assert.False(await browser.TryDisposeBrowserModuleAsync());
    }
    [Fact]
    public async Task Real_script_bugs_are_not_hidden_by_optional_ui_helpers()
    {
        var browser=new Browser{Failure=new JSException("Script bug")};
        await Assert.ThrowsAsync<JSException>(()=>browser.TryInvokeUiVoidAsync("window.print").AsTask());
        await Assert.ThrowsAsync<JSException>(()=>browser.TryInvokeUiBooleanAsync("copy").AsTask());
        await Assert.ThrowsAsync<JSException>(()=>browser.ConfirmOrCancelAsync("Save?").AsTask());
    }
    [Fact]
    public async Task Saas_worker_stops_normally_during_startup_delay()
    {
        using var worker=new SaasAutomationWorker(Mock.Of<IServiceScopeFactory>(),NullLogger<SaasAutomationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        // .NET may cancel Task.Run before ExecuteAsync starts during an immediate stop.
        Assert.NotNull(worker.ExecuteTask);Assert.True(worker.ExecuteTask.IsCompleted);Assert.False(worker.ExecuteTask.IsFaulted);
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Assert.False(await SaasAutomationWorker.WaitAsync(TimeSpan.FromHours(6),canceled.Token));
        Assert.True(await SaasAutomationWorker.WaitAsync(TimeSpan.Zero,CancellationToken.None));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Filter_initialization_can_retry_and_cleanup_releases_module_after_cancellation(bool disconnected)
    {
        var browser=new Browser{Failure=disconnected?new JSDisconnectedException("Disconnected"):new TaskCanceledException()};
        using var services=Services(browser);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            FilterProbe? filter=null;
            await renderer.RenderComponentAsync<FilterProbe>(ParameterView.FromDictionary(new Dictionary<string,object?>
            {
                [nameof(FilterProbe.Capture)]=(Action<FilterProbe>)(v=>filter=v),
                [nameof(CompactFilterBar.SearchContent)]=(RenderFragment)(b=>b.AddMarkupContent(0,"<input/>")),
                [nameof(CompactFilterBar.AdvancedFilters)]=(RenderFragment)(b=>b.AddMarkupContent(0,"<input/>"))
            }));
            await filter!.AfterRender(true);Assert.Single(browser.Calls);
            browser.Failure=null;await filter.AfterRender(false);Assert.Contains("initialize",browser.Calls);
            browser.Failure=new TaskCanceledException();await filter.DisposeAsync();Assert.Equal(1,browser.Disposals);
            var count=browser.Calls.Count;await filter.AfterRender(false);await filter.DisposeAsync();Assert.Equal(count,browser.Calls.Count);
        });
    }
    [Fact]
    public async Task Failed_local_storage_does_not_block_page_size_change_or_resetting_the_page()
    {
        var browser=new Browser{Failure=new TaskCanceledException()};using var services=Services(browser);
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            SizeProbe? selector=null;var size=20;var page=3;var changes=0;
            await renderer.RenderComponentAsync<SizeProbe>(ParameterView.FromDictionary(new Dictionary<string,object?>
            {
                [nameof(SizeProbe.Capture)]=(Action<SizeProbe>)(v=>selector=v),[nameof(PageSizeSelector.Value)]=size,[nameof(PageSizeSelector.CurrentPage)]=page,
                [nameof(PageSizeSelector.ValueChanged)]=EventCallback.Factory.Create<int>(this,v=>size=v),
                [nameof(PageSizeSelector.CurrentPageChanged)]=EventCallback.Factory.Create<int>(this,v=>page=v),
                [nameof(PageSizeSelector.Changed)]=EventCallback.Factory.Create(this,()=>changes++)
            }));
            await selector!.AfterRender(true);await selector.AfterRender(false);Assert.Equal(2,browser.Calls.Count);
            await selector.Change("50");Assert.Equal(50,size);Assert.Equal(1,page);Assert.Equal(1,changes);
        });
    }
    [Fact]
    public void Optional_print_and_dropdown_calls_use_the_cancellation_safe_path()
    {
        var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.FullName;
        foreach(var path in Directory.EnumerateFiles(Path.Combine(root,"HawalaExchange.Web","Components"),"*.razor",SearchOption.AllDirectories))
            Assert.False(Regex.IsMatch(File.ReadAllText(path),"(?:JS|JSRuntime)\\.InvokeVoidAsync\\(\\s*\"(?:window\\.print|alert|hawalaTools\\.(?:printElement|printReceipt|openShareUrl|openTelegramApp|openFloatingDropdown|closeFloatingDropdown))\""),path);
    }
    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
    private static ServiceProvider Services(Browser browser)=>new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(browser).AddSingleton<NavigationManager>(new TestNavigation()).BuildServiceProvider();
    public sealed class FilterProbe:CompactFilterBar
    {
        [Parameter] public Action<FilterProbe> Capture {get;set;}=default!;
        protected override void OnInitialized(){base.OnInitialized();Capture(this);}
        public Task AfterRender(bool first)=>base.OnAfterRenderAsync(first);
    }
    public sealed class SizeProbe:PageSizeSelector
    {
        [Parameter] public Action<SizeProbe> Capture {get;set;}=default!;
        protected override void OnInitialized()=>Capture(this);
        public Task AfterRender(bool first)=>base.OnAfterRenderAsync(first);
        public Task Change(string value)=>(Task)typeof(PageSizeSelector).GetMethod("HandleChangedAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(this,[new ChangeEventArgs{Value=value}])!;
    }
    private sealed class TestNavigation:NavigationManager {public TestNavigation()=>Initialize("https://test.local/","https://test.local/hawalas");}
    private sealed class Browser:IJSRuntime,IJSObjectReference
    {
        public Exception? Failure {get;set;}
        public List<string> Calls {get;}=[];
        public int Disposals {get;private set;}
        public ValueTask<T> InvokeAsync<T>(string name,object?[]? args)=>InvokeAsync<T>(name,CancellationToken.None,args);
        public ValueTask<T> InvokeAsync<T>(string name,CancellationToken token,object?[]? args)
        {
            Calls.Add(name);if(Failure is not null)return ValueTask.FromException<T>(Failure);
            return ValueTask.FromResult(name=="import"?(T)(object)this:default!);
        }
        public ValueTask DisposeAsync(){Disposals++;return Failure is null?ValueTask.CompletedTask:ValueTask.FromException(Failure);}
    }
}
