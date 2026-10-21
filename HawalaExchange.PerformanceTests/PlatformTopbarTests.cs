namespace HawalaExchange.PerformanceTests;

public sealed class PlatformTopbarTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path="") => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;

    [Fact]
    public void Platform_pages_share_an_interactive_topbar_and_keep_refresh_callbacks()
    {
        var folder=Path.Combine(Root(),"HawalaExchange.Web/Components/Pages/Platform");
        foreach(var file in Directory.GetFiles(folder,"*.razor"))
        {
            var source=File.ReadAllText(file);
            if(!source.StartsWith("@page ")) continue;
            Assert.Contains("<PlatformPageTopbar Refresh=\"RefreshPageDataAsync\"",source);
            Assert.DoesNotContain("<PageRefreshButton",source);
        }
        var component=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/Components/Shared/PlatformPageTopbar.razor"));
        Assert.Contains("<header class=\"platform-topbar platform-page-topbar no-print\"",component);
        Assert.Contains("<PageRefreshButton Refresh=\"Refresh\" Inline=\"true\" Disabled=\"Disabled\"",component);
        Assert.DoesNotContain("NavigateTo",component);
    }
}
