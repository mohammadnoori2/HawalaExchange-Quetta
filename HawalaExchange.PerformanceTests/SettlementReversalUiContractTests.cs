using System.Runtime.CompilerServices;

namespace HawalaExchange.PerformanceTests;

public sealed class SettlementReversalUiContractTests
{
    [Fact]
    public void Reversal_reason_updates_on_each_input_without_waiting_for_blur()
    {
        var source = ReadPage("Correspondents", "CorrespondentSettlementHistory.razor");
        Assert.Matches("<input[^>]*@bind=\"reverseReason\"[^>]*@bind:event=\"oninput\"[^>]*/>", source);
        Assert.Contains("برای فعال‌شدن دکمه برگشت، دلیل را وارد کنید.", source);
        Assert.Contains("disabled=\"@(busy || string.IsNullOrWhiteSpace(reverseReason))\"", source);
    }

    [Fact]
    public void Adjusted_conversions_also_offer_reversal_and_guard_against_duplicate_clicks()
    {
        var source = ReadPage("Correspondents", "CorrespondentSettlementHistory.razor");
        Assert.Contains("@if(details.Status is \"Active\" or \"Adjusted\"){<button class=\"btn btn-outline-danger\"", source);
        Assert.Contains("if(busy || details==null || details.Status is not (\"Active\" or \"Adjusted\"))return;", source);
    }

    [Fact]
    public void Bulk_delete_retains_settlement_safety_guard_and_explains_the_required_steps()
    {
        var source = ReadPage("HawalaTransactions", "HawalaImport.razor");
        Assert.Contains("disabled=\"@(isHistoryBusy || deleteImpact?.HasSettlementConversion == true)\"", source);
        Assert.Contains("حذف فعلاً غیرفعال است: این فایل حواله تبدیل‌شده به ارز توافقی دارد.", source);
        Assert.Contains("اگر کمیشن مرتبط ثبت شده است، ابتدا آن را از تاریخچه کمیشن برگشت دهید.", source);
    }

    private static string ReadPage(string folder, string name, [CallerFilePath] string source = "")
    {
        var root = Directory.GetParent(Path.GetDirectoryName(source)!)!.FullName;
        return File.ReadAllText(Path.Combine(root, "HawalaExchange.Web", "Components", "Pages", folder, name));
    }
}
