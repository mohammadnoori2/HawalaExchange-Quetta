using System.Text.RegularExpressions;

namespace HawalaExchange.PerformanceTests;

public sealed class PageRefreshHeaderTests
{
    [Fact]
    public void Operational_pages_have_one_inline_refresh_inside_their_existing_header()
    {
        var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        var pages = Directory.GetFiles(Path.Combine(root, "HawalaExchange.Web", "Components", "Pages"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Platform{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}Tenants{Path.DirectorySeparatorChar}"))
            .Select(path => (path, text: File.ReadAllText(path)))
            .Where(page => page.text.Contains("<PageRefreshButton"))
            .ToList();

        Assert.Equal(41, pages.Count);
        foreach (var (path, text) in pages)
        {
            var refresh = Assert.Single(Regex.Matches(text, "<PageRefreshButton\\b[^>]*>"));
            Assert.Contains("Inline=\"true\"", refresh.Value);
            var preceding = text[..refresh.Index];
            Assert.True(preceding.Contains("<div") || preceding.Contains("<AppPageHeader"), path);
            // Retry buttons in import history or modals are not page refresh controls.
            Assert.DoesNotContain("bi-arrow-clockwise", preceding);
            var callback = Regex.Match(refresh.Value, "Refresh=\"(?<name>[A-Za-z][A-Za-z0-9_]*)\"");
            Assert.True(callback.Success, path);
            Assert.Contains($"Task {callback.Groups["name"].Value}()", text);
        }
    }

    [Fact]
    public void Refresh_remains_a_data_callback_and_never_saves_or_reloads_the_document()
    {
        var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        var text = File.ReadAllText(Path.Combine(root, "HawalaExchange.Web", "Components", "Shared", "PageRefreshButton.razor"));
        Assert.Contains("if (busy || Disabled) return;", text);
        Assert.Contains("await Refresh.InvokeAsync();", text);
        Assert.Contains("EntityState.Unchanged", text);
        Assert.DoesNotContain("SaveChanges", text);
        Assert.DoesNotContain("NavigateTo", text);
        Assert.DoesNotContain("location.reload", text);
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
