using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HawalaExchange.Application.Services;

namespace HawalaExchange.PerformanceTests;

public sealed class MoneyPreviewParityTests
{
    [Fact]
    public async Task Browser_preview_matches_server_conversion_quotation_and_Dari_words()
    {
        var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        var module = new Uri(Path.Combine(root, "HawalaExchange.Web/wwwroot/js/money-preview.js")).AbsoluteUri;
        var cases = new List<object>();
        var expected = new List<string[]>();
        var random = new Random(4512);
        for (var i = 0; i < 120; i++)
        {
            var fromCode = i % 2 == 0 ? "USD" : "AFN";
            var toCode = i % 2 == 0 ? "AFN" : "USD";
            var fromPriority = i % 3 == 0 ? 1 : 2;
            var toPriority = 2;
            decimal amount = random.Next(1, 9000000) / 100m;
            decimal rate = random.Next(1, 100000) / 1000m;
            decimal toAmount = random.Next(1, 1000000) / 100m;
            var conversion = CurrencyQuotationCalculator.ConvertFromAmount(1, fromCode, fromPriority, amount, 2, toCode, toPriority, rate);
            var quotation = CurrencyQuotationCalculator.Calculate(1, fromCode, fromPriority, amount, 2, toCode, toPriority, toAmount);
            cases.Add(new { amount = Invariant(amount), rate = Invariant(rate), toAmount = Invariant(toAmount),
                from = new { id = "1", code = fromCode, priority = fromPriority },
                to = new { id = "2", code = toCode, priority = toPriority } });
            expected.Add([Invariant(AmountValueHelper.RoundConvertedAmount(conversion.ToAmount)),
                Invariant(Math.Round(quotation.Rate, 8)), DariNumberToWords.ToWords(amount)]);
        }
        var start = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false) };
        start.ArgumentList.Add("--input-type=module"); start.ArgumentList.Add("-e");
        start.ArgumentList.Add($"import {{convertPreview,quotePreview,moneyWords}} from '{module}';" +
            "let text='';for await(const chunk of process.stdin)text+=chunk;" +
            "console.log(JSON.stringify(JSON.parse(text).map(x=>[convertPreview(x.amount,x.rate,x.from,x.to),quotePreview(x.amount,x.toAmount,x.from,x.to),moneyWords(x.amount)])));");
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(cases)); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, error);
        var actual = JsonSerializer.Deserialize<string[][]>(output)!;
        Assert.Equal(expected.Count, actual.Length);
        for (var i = 0; i < expected.Count; i++) Assert.Equal(expected[i], actual[i]);
    }
    private static string Invariant(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
