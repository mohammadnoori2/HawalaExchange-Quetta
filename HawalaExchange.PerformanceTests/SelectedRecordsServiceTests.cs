using System.IO.Compression;
using ClosedXML.Excel;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class SelectedRecordsServiceTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Excel_contains_only_selected_rows_in_exact_headerless_import_order_and_literal_text()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var first = NewHawala(98_600_001, "=not-a-formula", "FIRST");
        var second = NewHawala(98_600_002, "Second", "SECOND");
        context.Hawalas.AddRange(first, second);
        await context.SaveChangesAsync();
        var file = await new SelectedRecordsService(context).ExportAsync([first.Id]);
        Assert.EndsWith(".xlsx", file.FileName);
        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(1, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(first.Number.ToString(), sheet.Cell(1, 1).GetString());
        Assert.Equal("FIRST", sheet.Cell(1, 2).GetString());
        Assert.Equal("=not-a-formula", sheet.Cell(1, 3).GetString());
        Assert.False(sheet.Cell(1, 3).HasFormula);
        Assert.Equal("Receiver", sheet.Cell(1, 4).GetString());
        Assert.Equal(fixture.RemoteLocation.Name, sheet.Cell(1, 5).GetString());
        Assert.Equal(1234.56m, sheet.Cell(1, 6).GetValue<decimal>());
        Assert.Equal("USD", sheet.Cell(1, 7).GetString());
        Assert.Equal(12m, sheet.Cell(1, 8).GetValue<decimal>());
        Assert.Equal("USD", sheet.Cell(1, 9).GetString());
        Assert.DoesNotContain("Secret", string.Join(" ", sheet.CellsUsed().Select(x => x.GetString())));
    }

    [Fact]
    public async Task Multiple_correspondents_and_types_export_separate_files_and_sent_number_and_currency_are_preserved()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var received = NewHawala(98_600_010, "Source", "RCV");
        context.Hawalas.Add(received);
        await context.SaveChangesAsync();
        var sent = NewHawala(101, "Source", "SEND");
        sent.HawalaType = "HawalaSend";
        sent.CorrespondentId = fixture.DestinationCorrespondent.Id;
        sent.SourceHawalaId = received.Id;
        sent.ToCurrencyId = 1; sent.ToAmount = 66000m;
        context.Hawalas.Add(sent);
        await context.SaveChangesAsync();
        var service = new SelectedRecordsService(context);
        var records = await service.ReadAsync([received.Id, sent.Id]);
        Assert.Equal(66000m, records.Single(x => x.Id == sent.Id).Amount);
        Assert.Equal("AFN", records.Single(x => x.Id == sent.Id).CurrencyCode);
        var file = await service.ExportAsync([received.Id, sent.Id]);
        Assert.Equal("application/zip", file.ContentType);
        using var stream = new MemoryStream(file.Content);
        using var archive = new ZipArchive(stream);
        Assert.Equal(2, archive.Entries.Count);
        using var workbook = new XLWorkbook(archive.Entries.Single(x => x.Name.Contains("HawalaSend")).Open());
        Assert.Equal("101", workbook.Worksheet(1).Cell(1, 1).GetString());
        Assert.Equal("AFN", workbook.Worksheet(1).Cell(1, 7).GetString());
    }

    [Fact]
    public async Task Foreign_deleted_or_invalid_ids_cannot_leak_or_silently_export_a_partial_selection()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var row = NewHawala(98_600_020, "Protected", "SECURE");
        context.Hawalas.Add(row);
        await context.SaveChangesAsync();
        var service = new SelectedRecordsService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync([row.Id, long.MaxValue]));
        using (context.UseTenantScope(999999))
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync([row.Id]));
        Assert.True(await context.Hawalas.AnyAsync(x => x.Id == row.Id));
    }

    [Fact]
    public async Task Transaction_export_preserves_each_financial_detail_without_mixing_it_with_hawala_import_format()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var transaction = new Transaction
        {
            TransactionNo = "SELECTED-DETAILS-TEST", TransactionType = "Exchange", BranchId = 1,
            CreatedBy = fixture.UserId, Status = "Paid", CustomerFullName = "Selected customer",
            TransactionDetails = [new TransactionDetail
            {
                FromCurrencyId = 2, FromAmount = 100, ToCurrencyId = 1, ToAmount = 6600,
                ExchangeRate = 66, CommissionCurrencyId = 2, CommissionAmount = 5
            }, new TransactionDetail { FromCurrencyId = 1, FromAmount = 200 }]
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();
        var service = new SelectedRecordsService(context);
        var records = await service.ReadAsync([transaction.Id], true);
        Assert.Equal(2, records.Single().TransactionDetails.Count);
        Assert.Equal("USD", records.Single().TransactionDetails[0].FromCurrency);
        var file = await service.ExportAsync([transaction.Id], true);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet(1);
        Assert.Equal(3, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(14, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal(100m, sheet.Cell(2, 7).GetValue<decimal>());
        Assert.Equal(6600m, sheet.Cell(2, 9).GetValue<decimal>());
        Assert.Equal(200m, sheet.Cell(3, 7).GetValue<decimal>());
        Assert.Equal(transaction.TransactionNo, sheet.Cell(3, 1).GetString());
    }

    private Hawala NewHawala(long number, string sender, string reference) => new()
    {
        Number = number, HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id,
        PaymentLocationId = fixture.RemoteLocation.Id, SenderName = sender, ReceiverName = "Receiver",
        ReferenceNumber = reference, FromCurrencyId = 2, FromAmount = 1234.56m,
        ToCurrencyId = 2, ToAmount = 1234.56m, ExchangeRate = 1,
        AgentCommissionAmount = 12, AgentCommissionCurrencyId = 2,
        Notes = "Secret internal note", SenderPhone = "Secret phone", Status = "Pending",
        CreatedBy = fixture.UserId, CreatedAt = DateTime.UtcNow
    };
}
