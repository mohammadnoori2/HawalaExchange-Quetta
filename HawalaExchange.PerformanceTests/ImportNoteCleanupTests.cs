using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class ImportNoteCleanupTests
{
    [Fact]
    public async Task Cleanup_removes_only_exact_import_notes_keeps_user_edits_and_preserves_provenance()
    {
        var database = new SqlServerPerformanceFixture();
        try
        {
            await database.InitializeDatabaseAsync("20261005180000_RoundFinalCommissionTotals");
            await using var context = database.CreateContext();
            using var bypass = context.BypassSubscriptionEnforcement();
            const string file = "customer-file.xlsx";
            var systemNote = $"آپلود گروهی از فایل {file}";
            Hawala Row(long number, string kind, string note) => new()
            {
                Number = number, HawalaType = kind, CorrespondentId = database.SourceCorrespondent.Id,
                FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 10, ToAmount = 10,
                Status = "Pending", CreatedBy = database.UserId, Notes = note
            };
            var received = Row(98610001, "HawalaReceive", systemNote);
            var sent = Row(98610002, "HawalaSend", systemNote);
            var edited = Row(98610003, "HawalaReceive", systemNote + "\nیادداشت مشتری");
            var unrelated = Row(98610004, "HawalaReceive", systemNote);
            context.Hawalas.AddRange(received, sent, edited, unrelated);
            await context.SaveChangesAsync();
            var batch = new HawalaImportBatch
            {
                FileName = file, FileHash = "test", CorrespondentId = database.SourceCorrespondent.Id,
                CreatedBy = database.UserId, Status = "Confirmed", RowCount = 2,
                Rows = [new HawalaImportRow { ExcelRowNumber = 1, HawalaId = received.Id, GeneratedSendHawalaId = sent.Id },
                        new HawalaImportRow { ExcelRowNumber = 2, HawalaId = edited.Id }]
            };
            context.HawalaImportBatches.Add(batch);
            await context.SaveChangesAsync();
            var service = new SelectedRecordsService(context);
            var shared = await service.ReadAsync([received.Id, sent.Id, edited.Id, unrelated.Id]);
            Assert.Equal("", shared.Single(x => x.Id == received.Id).Notes);
            Assert.Equal("", shared.Single(x => x.Id == sent.Id).Notes);
            Assert.Equal(edited.Notes, shared.Single(x => x.Id == edited.Id).Notes);
            Assert.Equal(systemNote, shared.Single(x => x.Id == unrelated.Id).Notes);
            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();
            Assert.Null((await context.Hawalas.SingleAsync(x => x.Id == received.Id)).Notes);
            Assert.Null((await context.Hawalas.SingleAsync(x => x.Id == sent.Id)).Notes);
            Assert.Equal(edited.Notes, (await context.Hawalas.SingleAsync(x => x.Id == edited.Id)).Notes);
            Assert.Equal(systemNote, (await context.Hawalas.SingleAsync(x => x.Id == unrelated.Id)).Notes);
            Assert.Equal(file, (await context.HawalaImportBatches.SingleAsync()).FileName);
            Assert.Equal(2, await context.HawalaImportRows.CountAsync());
        }
        finally { await database.DisposeAsync(); }
    }
}
