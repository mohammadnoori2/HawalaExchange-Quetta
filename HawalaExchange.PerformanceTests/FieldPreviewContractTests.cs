namespace HawalaExchange.PerformanceTests;

public sealed class FieldPreviewContractTests
{
    private static string Source(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !Directory.Exists(Path.Combine(root.FullName,"HawalaExchange.Web"))) root=root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName,"ui-previews","DatePicker",relative));
    }

    [Fact]
    public void Every_preview_route_uses_the_same_header_refresh_action()
    {
        var header=Source("DemoPageHeader.razor");
        var actions=header[header.IndexOf("class=\"page-actions\"",StringComparison.Ordinal)..header.IndexOf("</header>",StringComparison.Ordinal)];
        Assert.Contains("demo-refresh",actions);
        Assert.Contains("CreateLabel",actions);
        Assert.Contains("بایگانی",actions);
        Assert.Contains("<DemoPageHeader",Source("PickerDemo.razor"));
        Assert.Contains("<DemoPageHeader",Source("FormLayoutDemo.razor"));
    }

    [Fact]
    public void Refresh_replaces_only_the_fixture_without_resetting_filters_or_drafts()
    {
        var source=Source("FormLayoutDemo.razor");
        var refresh=source[source.IndexOf("private async Task ReloadAsync",StringComparison.Ordinal)..source.IndexOf("private Task OpenForm",StringComparison.Ordinal)];
        Assert.Contains("records=Seed()",refresh);
        Assert.DoesNotContain("ResetFilters",refresh);
        Assert.DoesNotContain("model=new",refresh);
        Assert.DoesNotContain("NavigateTo",refresh);
    }

    [Fact]
    public void Form_fields_keep_labeled_error_slots_and_real_shared_date_and_money_inputs()
    {
        var form=Source("FormFields.razor");
        Assert.Contains("<PersianDatePicker",form);
        Assert.Contains("<NullableMoneyInput",form);
        Assert.Contains("aria-describedby=\"name-error\"",form);
        Assert.Contains("<ValidationMessage",form);
        var css=Source(Path.Combine("wwwroot","preview-fields.css"));
        Assert.Contains("min-height:1.65em",css);
        Assert.Contains(".sample-form-dialog .modal-body{min-height:0;overflow:auto}",css);
        Assert.Contains("--sample-control-height:44px",css);
        Assert.Contains(".sample-field>label .text-danger{display:inline!important;margin:0!important",css);
    }
}
