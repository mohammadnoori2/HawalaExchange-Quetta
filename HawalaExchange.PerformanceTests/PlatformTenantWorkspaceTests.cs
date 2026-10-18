using System.Text.RegularExpressions;

namespace HawalaExchange.PerformanceTests;

public sealed class PlatformTenantWorkspaceTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path="") => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
    private static string Read(string path)=>File.ReadAllText(Path.Combine(Root(),path));
    [Fact]
    public void Tenant_details_share_existing_guards_and_restore_filter_context()
    {
        var source=Read("HawalaExchange.Web/Components/Pages/Tenants/Tenants.razor");
        Assert.Contains("@page \"/tenants/{DetailTenantId:long}\"",source);
        Assert.Contains("Authorize(Policy = \"PlatformWrite\")",source);
        Assert.Contains("<AuthorizeView Policy=\"PlatformImpersonate\"><HawalaExchange.Web.Components.Pages.Platform.TenantManagement",source);
        Assert.Contains("data-tenant-detail",source);
        Assert.Contains("private string ListUrl => \"/tenants\" + FilterQuery",source);
        foreach(var query in new[]{"q","status","plan","archived","sort"}) Assert.Contains($"SupplyParameterFromQuery(Name=\"{query}\")",source);
        Assert.Contains("Navigation.NavigateTo(ListUrl,replace:true)",source);
        Assert.Contains("!selected.IsArchived",source);
        Assert.Contains("JS.ConfirmOrCancelAsync(",source);
    }
    [Fact]
    public void Create_wizard_validates_without_writes_until_final_confirmation()
    {
        var source=Read("HawalaExchange.Web/Components/Pages/Tenants/Tenants.razor");
        var next=source[source.IndexOf("private void NextCreateStep()")..source.IndexOf("[Parameter] public long DetailTenantId")];
        Assert.Contains("Validator.TryValidateProperty",next);
        Assert.DoesNotContain("TenantService.",next);
        Assert.Contains("createStep==4",source);
        Assert.Contains("if (!createEditContext.Validate()) return;",source);
        Assert.Contains("await TenantService.CreateAsync(createModel)",source);
        Assert.Contains("hidden=\"@(createStep!=1)\"",source);
    }
    [Fact]
    public void Every_platform_table_starts_with_a_row_number()
    {
        var folder=Path.Combine(Root(),"HawalaExchange.Web/Components/Pages/Platform");
        foreach(var file in Directory.EnumerateFiles(folder,"*.razor"))
        foreach(Match table in Regex.Matches(File.ReadAllText(file),@"<(?:GlobalDataGrid|table)\b[\s\S]*?</(?:GlobalDataGrid|table)>"))
        {
            if(!table.Value.Contains("<thead")) continue; // Invoice totals are a key/value summary, not a record list.
            var first=Regex.Match(table.Value,@"<thead[^>]*>\s*<tr>\s*<th[^>]*>(.*?)</th>");
            Assert.True(first.Success,file);
            Assert.Contains(first.Groups[1].Value,new[]{"شماره ردیف","#"});
        }
        Assert.Contains("(filter.Page - 1) * filter.PageSize",Read("HawalaExchange.Web/Components/Pages/Platform/PlatformAudit.razor"));
        Assert.Contains("(filter.Page - 1) * filter.PageSize",Read("HawalaExchange.Web/Components/Pages/Platform/SaasReports.razor"));
    }
    [Fact]
    public void Embedded_billing_keeps_tenant_context_and_its_original_authorization()
    {
        var source=Read("HawalaExchange.Web/Components/Pages/Platform/Billing.razor");
        Assert.Contains("Authorize(Policy = \"PlatformBilling\")",source);
        Assert.Contains("x.TenantId==ManagedTenantId",source);
        Assert.Contains("if(!VisibleOptions.Any(x=>x.SubscriptionId==createModel.SubscriptionId))",source);
        Assert.Contains("if(!VisibleInvoices.Any(x=>x.Id==paymentModel.InvoiceId))",source);
        Assert.Contains("if(!ManagedTenantId.HasValue) dashboard = await BillingService.GetDashboardAsync()",source);
        Assert.Contains("<AuthorizeView Policy=\"PlatformBilling\"><Billing",Read("HawalaExchange.Web/Components/Pages/Platform/TenantManagement.razor"));
    }
    [Fact]
    public void Sorting_and_scroll_restoration_are_scoped_without_accounting_mutations()
    {
        var source=Read("HawalaExchange.Web/wwwroot/js/platform-workspace.js");
        Assert.Contains("location.pathname !== '/tenants'",source);
        Assert.Contains("sessionStorage",source);
        Assert.Contains("preventScroll: true",source);
        var sorting=Read("HawalaExchange.Web/wwwroot/js/global-data-grid.js");
        Assert.Contains("table.closest('.platform-workspace')",sorting);
        Assert.Contains("[data-platform-row-number]",sorting);
        Assert.Contains("EnableSorting ? \"true\" : \"false\"",Read("HawalaExchange.Web/Components/Shared/GlobalDataGrid.razor"));
    }
}
