using System.Globalization;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;

namespace HawalaExchange.PerformanceTests;

public sealed class HawalaInputTextFormatterTests
{
    [Fact]
    public void Tabular_text_keeps_seven_columns_and_english_amounts_without_extra_fields()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            var rows = new[] { new SelectedRecordDto { Number = "2172", ReferenceNumber = "A44559",
                SenderName = "Abdollah Nazari", ReceiverName = "Abdul Jamil", PaymentLocationName = "Qonduz",
                Amount = 21560m, CurrencyCode = "AFN", Notes = "Excluded", AgentCommission = 400m },
                new SelectedRecordDto { Number = "2", CurrencyCode = "USD", Amount = 120.5m } };
            var lines = HawalaInputTextFormatter.Format(rows).Split("\r\n");
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.Equal(7, line.Split('\t').Length));
            Assert.Equal("2172\tA44559\tAbdollah Nazari\tAbdul Jamil\tQonduz\t21560\tAFN", lines[0]);
            Assert.EndsWith("120.5\tUSD", lines[1]);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Embedded_tabs_newlines_and_formula_prefixes_do_not_create_cells_or_formulas()
    {
        var row = new SelectedRecordDto { Number = "1234567890123456789", ReferenceNumber = "=1+1",
            SenderName = "+formula\tother", ReceiverName = "@formula\nother", PaymentLocationName = "-formula\rplace", CurrencyCode = "USD" };
        var cells = HawalaInputTextFormatter.Format([row]).Split('\t');
        Assert.Equal(7, cells.Length);
        Assert.Equal("'1234567890123456789", cells[0]);
        Assert.Equal("'=1+1", cells[1]);
        Assert.Equal("'+formula other", cells[2]);
        Assert.DoesNotContain('\n', string.Join("", cells));
        Assert.DoesNotContain('\r', string.Join("", cells));
    }
}
