namespace HawalaExchange.PerformanceTests;

public sealed class PlatformDesignContractTests
{
    [Fact]
    public void Repeated_controls_share_one_platform_sizing_and_shape_contract()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web", "wwwroot", "styles", "pages", "Platform", "PlatformWorkspace.css"));
        var contract = css[css.IndexOf("/* One component contract")..];
        foreach(var component in new[]{".form-control-modern", ".form-select", ".select-page-size", ".date-picker-input", ".tenant-money-amount", ".compact-reset-filter"})
            Assert.Contains(component,contract);
        Assert.Contains("height: var(--ui-control-height) !important;",contract);
        Assert.Contains("--filter-control-height: var(--ui-control-height);",contract);
        Assert.Contains("border-radius: var(--platform-field-radius) !important;",contract);
        Assert.Contains("font-size: var(--platform-field-font-size) !important;",contract);
        Assert.Contains("--platform-field-font-size: 1rem",contract);
        Assert.Contains("textarea.form-control { min-height:",contract);
        Assert.Contains(".invalid { border-color: var(--app-danger) !important; }",contract);
    }
    [Fact]
    public void Eighty_percent_density_is_desktop_only_and_uses_relative_units()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web", "wwwroot", "styles", "pages", "Platform", "PlatformWorkspace.css"));
        Assert.Contains("@media screen and (min-width: 769px)", css);
        Assert.Contains("html:has(.platform-workspace) { font-size: 12.8px; }", css);
        Assert.Contains("--ui-control-height: 2.375rem", css);
        Assert.Contains("--ui-sidebar-width: 14.25rem", css);
        Assert.Contains("min-height: 2.75rem", css);
        Assert.DoesNotContain("zoom:", css);
        Assert.DoesNotContain("transform: scale", css);
    }

    [Fact]
    public void Modal_scroll_constraint_reaches_forms_and_busy_fieldsets()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web", "wwwroot", "styles", "pages", "Platform", "PlatformWorkspace.css"));
        Assert.Contains("height: calc(100dvh - 2 * var(--platform-modal-gap))", css);
        Assert.Contains(".platform-workspace .modal-content > form > fieldset", css);
        Assert.Contains("overscroll-behavior-y: contain", css);
        Assert.Contains(".platform-workspace :is(.modal-header,.modal-footer) { flex-shrink: 0; }", css);
    }

    [Fact]
    public void Local_vazirmatn_covers_platform_text_without_replacing_icon_pseudo_elements()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web", "wwwroot", "styles", "pages", "Platform", "PlatformWorkspace.css"));
        Assert.Contains("--ui-font-family: \"PlatformVazirmatn\", Tahoma, sans-serif;", css);
        Assert.Contains("../../../fonts/Vazirmatn-Regular.ttf", css);
        Assert.Contains("font-display: swap", css);
        Assert.Contains("font-family: var(--ui-font-family) !important;", css);
        Assert.DoesNotContain("::before", css);
        Assert.True(File.Exists(Path.Combine(Root(), "HawalaExchange.Web", "wwwroot", "fonts", "Vazirmatn-Regular.ttf")));
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;

    [Fact]
    public void Workspace_is_limited_to_platform_pages_and_keeps_authorization()
    {
        var platform = Path.Combine(Root(), "HawalaExchange.Web", "Components", "Pages", "Platform");
        Assert.Contains("@layout PlatformLayout", File.ReadAllText(Path.Combine(platform, "_Imports.razor")));
        foreach (var path in Directory.EnumerateFiles(platform, "*.razor"))
        {
            var source = File.ReadAllText(path);
            if (!source.StartsWith("@page ")) continue;
            Assert.Contains("@attribute [Authorize(Policy", source);
            Assert.Contains("<h1 class=\"page-title\">", source);
            Assert.Contains("<PageRefreshButton Refresh=", source);
        }
        var common = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web", "Components", "_Imports.razor"));
        Assert.DoesNotContain("@layout PlatformLayout", common);
        var tenants = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web", "Components", "Pages", "Tenants", "Tenants.razor"));
        Assert.Contains("@layout PlatformLayout", tenants);
        Assert.Contains("Authorize(Policy = \"PlatformWrite\")", tenants);
    }

    [Fact]
    public void Sensitive_actions_remain_confirmed_and_secondary_controls_use_native_disclosures()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web", "Components", "Pages", "Platform", "TenantManagement.razor"));
        Assert.Contains("<details class=\"platform-action-disclosure\">", source);
        Assert.Contains("<details class=\"platform-permission-group\"", source);
        Assert.Contains("if(!acknowledged||pending is null||busy)return;", source);
        Assert.Contains("aria-controls=\"tenant-management-panel\"", source);
        Assert.Contains("@onsubmit:preventDefault", source);
        Assert.Contains("@bind=\"search\"", source);
        Assert.DoesNotContain("@oninput=\"Search\"", source);
    }

    [Theory]
    [InlineData("243746", "FFFFFF")]
    [InlineData("526675", "F4F7F9")]
    [InlineData("FFFFFF", "086A91")]
    [InlineData("176447", "EAF6EF")]
    [InlineData("815000", "FFF6E4")]
    [InlineData("AA3434", "FFF0EF")]
    public void Text_palette_meets_normal_text_contrast(string foreground, string background)
    {
        static double Luminance(string hex)
        {
            var values = Enumerable.Range(0, 3).Select(i => Convert.ToInt32(hex.Substring(i * 2, 2), 16) / 255d)
                .Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
            return values[0] * .2126 + values[1] * .7152 + values[2] * .0722;
        }
        var a = Luminance(foreground); var b = Luminance(background);
        Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5);
    }
}
