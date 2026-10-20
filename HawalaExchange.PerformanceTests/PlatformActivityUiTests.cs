namespace HawalaExchange.PerformanceTests;

public sealed class PlatformActivityUiTests
{
    [Fact]
    public void Tenant_workspace_replaces_platform_identity_bar_with_name_refresh_and_filtered_back_link()
    {
        var page=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/Components/Pages/Tenants/Tenants.razor"));
        var start=page.IndexOf("<header class=\"platform-topbar platform-tenant-topbar",StringComparison.Ordinal);
        Assert.True(start>=0);
        var end=page.IndexOf("</header>",start,StringComparison.Ordinal);
        var header=page[start..end];
        Assert.Contains("SelectedTenant?.Name",header);
        Assert.Contains("<h1",header);
        Assert.Contains("Refresh=\"RefreshPageDataAsync\"",header);
        Assert.Contains("Inline=\"true\"",header);
        Assert.Contains("href=\"@ListUrl\"",header);
        Assert.DoesNotContain("تمام اطلاعات و عملیات همین صرافی",page);
        var css=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/wwwroot/styles/pages/Platform/PlatformWorkspace.css"));
        Assert.Contains(".platform-workspace:has(.platform-tenant-topbar) > .platform-topbar",css);
        Assert.Contains(".platform-tenant-topbar-actions",css);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;

    [Fact]
    public void Dashboard_embeds_activity_without_changing_its_access_policy_and_refreshes_it()
    {
        var page = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web/Components/Pages/Platform/PlatformDashboard.razor"));
        Assert.Contains("Authorize(Policy = \"PlatformAccess\")", page);
        Assert.Contains("<PlatformActivityPanel @ref=\"activityPanel\"", page);
        Assert.Contains("activityPanel.RefreshAsync()", page);
        var panel = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web/Components/Shared/PlatformActivityPanel.razor"));
        Assert.Contains("<TenantActivityChart Rows=\"report.Tenants\" TenantLink=\"TenantActivityUrl\"", panel);
        Assert.Contains("<TenantActivityDetailCharts Report=\"report\"", panel);
    }

    [Fact]
    public void Styles_are_loaded_and_separate_tenant_page_places_vertical_bars_after_tenant_summary()
    {
        var layout=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/Components/Layout/PlatformLayout.razor"));
        Assert.Contains("HawalaExchange.Web.styles.css",layout);
        var page=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/Components/Pages/Platform/TenantActivity.razor"));
        Assert.Contains("@page \"/platform/tenants/{TenantId:long}/activity\"",page);
        Assert.Contains("Authorize(Policy = \"PlatformAccess\")",page);
        Assert.True(page.IndexOf("aria-label=\"خلاصه صرافی\"",StringComparison.Ordinal)<page.IndexOf("<PlatformActivityPanel",StringComparison.Ordinal));
        Assert.Contains("TenantId=\"TenantId\"",page);
        var detail=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/Components/Shared/TenantActivityDetailCharts.razor.css"));
        Assert.Contains("writing-mode: vertical-rl",detail);
    }

    [Fact]
    public void Management_page_hosts_all_activity_reports_immediately_after_summary_and_refreshes_them()
    {
        var page=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/Components/Pages/Tenants/Tenants.razor"));
        var summary=page.IndexOf("aria-label=\"خلاصه صرافی\"",StringComparison.Ordinal);
        var activity=page.IndexOf("<PlatformActivityPanel",StringComparison.Ordinal);
        var administration=page.IndexOf("<HawalaExchange.Web.Components.Pages.Platform.TenantManagement",StringComparison.Ordinal);
        Assert.True(summary>=0 && activity>summary && administration>activity);
        Assert.Contains("@ref=\"activityPanel\" TenantId=\"DetailTenantId\"",page);
        Assert.Contains("activityPanel.RefreshAsync()",page);
        Assert.Contains("FromDate=\"ActivityFromDate\" ToDate=\"ActivityToDate\"",page);
        Assert.Contains("Authorize(Policy = \"PlatformWrite\")",page);
        var previous=File.ReadAllText(Path.Combine(Root(),"HawalaExchange.Web/Components/Pages/Platform/TenantActivity.razor"));
        Assert.Contains("Authorization.AuthorizeAsync",previous);
        Assert.Contains("Navigation.NavigateTo",previous);
        Assert.Contains("/tenants/{TenantId}",previous);
    }

    [Fact]
    public void Activity_has_labeled_filters_drilldown_english_numbers_and_no_raw_financial_payloads()
    {
        var panel = File.ReadAllText(Path.Combine(Root(), "HawalaExchange.Web/Components/Shared/PlatformActivityPanel.razor"));
        Assert.Contains("for=\"platform-activity-tenant\"", panel);
        Assert.Contains("Id=\"platform-activity-from\"", panel);
        Assert.Contains("Id=\"platform-activity-to\"", panel);
        Assert.Contains("TenantActivityUrl(row.TenantId)", panel);
        Assert.Contains("CultureInfo.InvariantCulture", panel);
        Assert.Contains("<details", panel);
        Assert.Contains("role=\"alert\"", panel);
        Assert.Contains("role=\"status\"", panel);
        Assert.Contains("UTC", panel);
        Assert.DoesNotContain("MarkupString", panel);
        Assert.DoesNotContain("ex.Message", panel);
    }
}
