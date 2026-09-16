using ClosedXML.Excel;
using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class HawalaImportStagingPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Preview_bulk_stages_7_8_and_9_column_rows_and_validates_duplicates()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        await using var workbook = CreateWorkbook([
            [99_100_001, "STG-1", "Sender 1", "Receiver 1", fixture.OwnLocation.Name, 1_000, "USD"],
            [99_100_002, "STG-2", "Sender 2", "Receiver 2", fixture.RemoteLocation.Name, 2_000, "USD", 25],
            [99_100_003, "STG-3", "Sender 3", "Receiver 3", fixture.RemoteLocation.Name, 3_000, "USD", 30, "USD"],
            [99_100_003, "STG-4", "Sender 4", "Receiver 4", fixture.RemoteLocation.Name, 4_000, "USD"]
        ]);

        var preview = await fixture.CreateImportService(context)
            .PreviewAsync(workbook, "staging-columns.xlsx", fixture.SourceCorrespondent.Id);

        Assert.Equal(4, preview.RowCount);
        Assert.Equal(2, preview.InvalidRowCount);
        Assert.Contains(preview.Rows, x => x.ExcelRowNumber == 3 &&
            x.ValidationErrors!.Contains("شماره حواله در همین فایل تکراری است"));
        var defaultedCommission = Assert.Single(preview.Rows, x => x.ExcelRowNumber == 2);
        Assert.Equal(25, defaultedCommission.AgentCommissionAmount);
        Assert.Equal(2, defaultedCommission.AgentCommissionCurrencyId);
        Assert.Equal("USD", defaultedCommission.AgentCommissionCurrencyCode);
        Assert.Empty(context.ChangeTracker.Entries<HawalaImportRow>());
    }

    [Fact]
    public async Task Preview_and_confirm_preserve_paired_hawala_and_ledger_accounting()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var destinationLocation = await EnsureDestinationLocationAsync(context);
        await using var workbook = CreateWorkbook([
            [92_000_001, "STG-C-1", "Sender 1", "Receiver 1", fixture.OwnLocation.Name, 1_000, "USD"],
            [92_000_002, "STG-C-2", "Sender 2", "Receiver 2", destinationLocation.Name, 2_000, "USD", 25]
        ]);
        var service = fixture.CreateImportService(context);

        var preview = await service.PreviewAsync(
            workbook, "staging-confirm.xlsx", fixture.SourceCorrespondent.Id);
        Assert.Equal(0, preview.InvalidRowCount);
        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto
        {
            BatchId = preview.BatchId,
            Commissions = preview.Rows.Where(x => x.RequiresOutgoingHawala).Select(x =>
                new HawalaImportCommissionDto
                {
                    RowId = x.Id, Amount = x.AgentCommissionAmount, CurrencyId = x.AgentCommissionCurrencyId
                }).ToList()
        });

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.GeneratedSendCount);
        context.ChangeTracker.Clear();
        var rows = await context.HawalaImportRows.AsNoTracking()
            .Where(x => x.BatchId == preview.BatchId).ToListAsync();
        Assert.All(rows, x => Assert.NotNull(x.HawalaId));
        var generatedId = Assert.Single(rows, x => x.GeneratedSendHawalaId.HasValue).GeneratedSendHawalaId!.Value;
        var generated = await context.Hawalas.AsNoTracking().SingleAsync(x => x.Id == generatedId);
        Assert.Equal(25, generated.AgentCommissionAmount);
        Assert.Equal(2, generated.AgentCommissionCurrencyId);
        var relatedIds = rows.SelectMany(x => new long?[] { x.HawalaId, x.GeneratedSendHawalaId })
            .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var ledger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.HawalaId.HasValue && relatedIds.Contains(x.HawalaId.Value)).ToListAsync();
        Assert.Equal(0, ledger.Sum(x => x.TalabKar - x.BadehKar));

        workbook.Position = 0;
        var duplicateFile = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(
            workbook, "staging-confirm-copy.xlsx", fixture.SourceCorrespondent.Id));
        Assert.Contains("قبلاً", duplicateFile.Message);
    }

    [Fact]
    public async Task Staging_procedure_detects_existing_number_and_reference()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        context.Hawalas.Add(new Hawala
        {
            Number = 92_500_001, HawalaType = "HawalaReceive",
            CorrespondentId = fixture.SourceCorrespondent.Id,
            PaymentLocationId = fixture.OwnLocation.Id,
            SenderName = "Existing", ReceiverName = "Existing",
            FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 100, ToAmount = 100,
            ReferenceNumber = "STG-EXISTING", Status = "Pending", CreatedBy = fixture.UserId
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await using var workbook = CreateWorkbook([
            [92_500_001, "STG-NEW", "Sender 1", "Receiver 1", fixture.OwnLocation.Name, 1_000, "USD"],
            [92_500_002, "STG-EXISTING", "Sender 2", "Receiver 2", fixture.OwnLocation.Name, 2_000, "USD"]
        ]);

        var preview = await fixture.CreateImportService(context).PreviewAsync(
            workbook, "staging-existing.xlsx", fixture.SourceCorrespondent.Id);

        Assert.Equal(2, preview.InvalidRowCount);
        Assert.Contains(preview.Rows, x => x.ValidationErrors!.Contains("شماره حواله قبلاً"));
        Assert.Contains(preview.Rows, x => x.ValidationErrors!.Contains("رفرنس قبلاً"));
    }

    [Fact]
    public async Task Confirm_revalidates_many_outgoing_rows_with_bounded_database_queries()
    {
        const int rowCount = 100;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var destinationLocation = await EnsureDestinationLocationAsync(context);
        var rows = Enumerable.Range(0, rowCount).Select(index => new object[]
        {
            93_000_000L + index, $"STG-B-{index}", $"Sender {index}", $"Receiver {index}",
            destinationLocation.Name, 1_000m + index, "USD"
        }).ToList();
        await using var workbook = CreateWorkbook(rows);
        var service = fixture.CreateImportService(context);
        var preview = await service.PreviewAsync(workbook, "staging-bounded.xlsx", fixture.SourceCorrespondent.Id);

        fixture.Commands.Reset();
        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto { BatchId = preview.BatchId });

        Assert.Equal(rowCount, result.ImportedCount);
        Assert.Equal(rowCount, result.GeneratedSendCount);
        Assert.Equal(rowCount, result.MissingCommissionCount);
        Assert.InRange(fixture.Commands.Count, 1, 30);
    }

    [Fact]
    public async Task Measure_bulk_staging_for_10000_rows()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Import staging performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int rowCount = 10_000;
        const long numberBase = 94_000_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var legacyBatch = new HawalaImportBatch
        {
            CorrespondentId = fixture.SourceCorrespondent.Id,
            OwnPaymentLocationId = fixture.OwnLocation.Id,
            FileName = "legacy-staging.xlsx",
            FileHash = new string('A', 64),
            Status = "Preview",
            RowCount = rowCount,
            CreatedBy = fixture.UserId,
            Rows = Enumerable.Range(0, rowCount).Select(index => new HawalaImportRow
            {
                ExcelRowNumber = index + 1,
                HawalaNumber = numberBase + index,
                ReferenceNumber = $"LEGACY-STG-{index}",
                SenderName = $"Sender {index}",
                ReceiverName = $"Receiver {index}",
                PaymentLocationText = fixture.OwnLocation.Name,
                PaymentLocationId = fixture.OwnLocation.Id,
                Amount = 1_000 + index,
                CurrencyCode = "USD",
                CurrencyId = 2
            }).ToList()
        };
        var legacyWatch = Stopwatch.StartNew();
        context.HawalaImportBatches.Add(legacyBatch);
        await context.SaveChangesAsync();
        legacyWatch.Stop();
        context.ChangeTracker.Clear();

        var workbookRows = Enumerable.Range(0, rowCount).Select(index => new object[]
        {
            numberBase + rowCount + index, $"BULK-STG-{index}", $"Sender {index}",
            $"Receiver {index}", fixture.OwnLocation.Name, 1_000m + index, "USD"
        }).ToList();
        await using var workbook = CreateWorkbook(workbookRows);
        var bulkWatch = Stopwatch.StartNew();
        var preview = await fixture.CreateImportService(context).PreviewAsync(
            workbook, "bulk-staging.xlsx", fixture.SourceCorrespondent.Id);
        bulkWatch.Stop();
        var confirmWatch = Stopwatch.StartNew();
        var result = await fixture.CreateImportService(context).ConfirmAsync(
            new ConfirmHawalaImportDto { BatchId = preview.BatchId });
        confirmWatch.Stop();

        Assert.Equal(rowCount, preview.ValidRowCount);
        Assert.Equal(rowCount, result.ImportedCount);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            rows = rowCount,
            legacyEfPersistenceMilliseconds = Math.Round(legacyWatch.Elapsed.TotalMilliseconds, 2),
            bulkEndToEndMilliseconds = Math.Round(bulkWatch.Elapsed.TotalMilliseconds, 2),
            confirmAndAccountingMilliseconds = Math.Round(confirmWatch.Elapsed.TotalMilliseconds, 2),
            improvementPercent = Math.Round((1 - bulkWatch.Elapsed.TotalMilliseconds /
                legacyWatch.Elapsed.TotalMilliseconds) * 100, 2)
        }));
    }

    private async Task ConfigureOwnLocationAsync(HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
    {
        var setting = await context.CompanySettings.SingleOrDefaultAsync();
        if (setting == null)
        {
            setting = new CompanySetting { CompanyName = "Performance Test" };
            context.CompanySettings.Add(setting);
        }
        setting.OwnPaymentLocationId = fixture.OwnLocation.Id;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private async Task<PaymentLocation> EnsureDestinationLocationAsync(
        HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
    {
        var normalized = HawalaExchange.Application.Services.PaymentLocationNameNormalizer.Normalize(
            fixture.DestinationCorrespondent.Name);
        var existing = await context.PaymentLocations.FirstOrDefaultAsync(x => x.NormalizedName == normalized);
        if (existing != null)
            return existing;
        var location = new PaymentLocation
        {
            Name = fixture.DestinationCorrespondent.Name, NormalizedName = normalized,
            Address = "Remote", IsActive = true, CreatedBy = fixture.UserId
        };
        context.PaymentLocations.Add(location);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return location;
    }

    private static MemoryStream CreateWorkbook(IReadOnlyList<object[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Hawalas");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            sheet.Cell(rowIndex + 1, columnIndex + 1).Value = XLCellValue.FromObject(rows[rowIndex][columnIndex]);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
