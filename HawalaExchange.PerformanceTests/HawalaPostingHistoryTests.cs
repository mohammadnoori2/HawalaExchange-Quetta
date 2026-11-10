using AutoMapper;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class HawalaPostingHistoryTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Repeated_corrections_have_separate_original_reversal_and_replacement_rows_without_changing_balances()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var h = await Seed(db);
        var journal = fixture.CreateJournalService(db);
        var before = await db.LedgerEntries.Where(x => x.HawalaId == h.Id).SumAsync(x => x.TalabKar - x.BadehKar);
        var all = await journal.GetJournalAsync(DateTime.Today.AddDays(-10), DateTime.Today);
        var rows = all.Operations.Where(x => x.SourceId == h.Id && x.SourceType == "حواله").ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(2, rows.Count(x => x.Description.Contains("برگشت")));
        Assert.All(rows, x => Assert.Equal(2, x.LedgerEntries.Count));
        Assert.Equal(5, rows.Select(x => x.OperationKey).Distinct().Count());
        Assert.Null(await journal.GetAccountOperationDetailsAsync(fixture.DestinationAccount.Id, rows.First(x => x.Description.StartsWith("ثبت جدید")).OperationKey));
        var oldAccount = await journal.GetAccountOperationsPageAsync(new() { AccountId = fixture.DestinationAccount.Id, SearchTerm = h.Number.ToString(), PageSize = 100 });
        Assert.Equal(2, oldAccount.TotalCount);
        Assert.Equal(15000m, oldAccount.Items.Sum(x => x.CurrencySummaries.Sum(c => c.TotalTalabKar)));
        Assert.Equal(15000m, oldAccount.Items.Sum(x => x.CurrencySummaries.Sum(c => c.TotalBadehKar)));
        foreach (var row in oldAccount.Items)
        {
            var detail = await journal.GetAccountOperationDetailsAsync(fixture.DestinationAccount.Id, row.OperationKey);
            Assert.NotNull(detail); Assert.Equal(2, detail.LedgerEntries.Count);
            Assert.Equal(row.CreatedAt, detail.CreatedAt);
            Assert.All(detail.SourceDetails, x => Assert.Contains(x.Label, new[] { "شماره حواله", "نوع حواله", "مرحله ثبت حسابداری" }));
        }
        Assert.Equal(2, (await journal.GetAccountOperationsAsync(fixture.DestinationAccount.Id)).Count(x => x.SourceId == h.Id));
        var filtered = await journal.GetAccountOperationsPageAsync(new() { AccountId = fixture.DestinationAccount.Id, SearchTerm = h.Number.ToString(), FromDate = DateTime.Today.AddDays(-1), ToDate = DateTime.Today, Direction = "Debit" });
        Assert.Equal(1, filtered.TotalCount);
        Assert.Contains("برگشت", filtered.Items[0].Description);
        Assert.Equal(before, await db.LedgerEntries.Where(x => x.HawalaId == h.Id).SumAsync(x => x.TalabKar - x.BadehKar));
        Assert.Equal(10, await db.LedgerEntries.CountAsync(x => x.HawalaId == h.Id));
        using (db.UseTenantScope(999999))
            Assert.Null(await journal.GetAccountOperationDetailsAsync(fixture.DestinationAccount.Id, rows[0].OperationKey));
    }

    [Fact]
    public async Task Transactions_show_existing_postings_as_read_only_and_never_create_financial_documents()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var h = await Seed(db);
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var service = new TransactionService(db, mapper, Mock.Of<ILedgerService>(), Mock.Of<IAccountService>(), Mock.Of<IExchangeRateService>(), Mock.Of<IAuditLogService>(), Mock.Of<ISubscriptionAccessService>());
        var before = await db.Transactions.CountAsync();
        var reversalEntries = await db.LedgerEntries.Where(x => x.HawalaId == h.Id && x.Description!.StartsWith("اصلاح حواله")).Select(x => x.Id).ToListAsync();
        db.AuditLogs.Add(new AuditLog { TableName = "Hawalas", RecordId = h.Id, Action = "UPDATE", UserId = fixture.UserId, ProcessId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
            NewValue = JsonSerializer.Serialize(reversalEntries.ToDictionary(id => $"LedgerEntries\u001f{id}\u001fId", id => id)) });
        await db.SaveChangesAsync();
        var rows = (await service.GetAllAsync()).Where(x => x.Id < 0 && x.Remarks!.Contains(h.Number.ToString())).ToList();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, x => Assert.Equal("Recorded", x.Status));
        Assert.All(rows.Where(x => x.Remarks!.Contains("برگشت")), x => Assert.Equal("Performance Test", x.CreatedByName));
        foreach(var row in rows) Assert.Equal(2, (await service.GetByIdAsync(row.Id))!.LedgerEntries!.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseTransactionAsync(rows[0].Id, new() { CancelReason = "test" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(rows[0].Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkAsPaidAsync(rows[0].Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelTransactionAsync(rows[0].Id, new() { CancelReason = "test" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(rows[0].Id, new()));
        Assert.Equal(before, await db.Transactions.CountAsync());
        using(db.UseTenantScope(999999)) Assert.Null(await service.GetByIdAsync(rows[0].Id));
    }

    [Fact]
    public async Task Posting_boundaries_use_ledger_order_even_when_all_timestamps_match()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var h = await Seed(db, true);
        var entries = await db.LedgerEntries.Where(x => x.HawalaId == h.Id).OrderBy(x => x.Id).ToListAsync();
        var date = DateTime.Today.AddHours(1).ToUniversalTime();
        foreach (var e in entries) e.CreatedAt = date;
        await db.SaveChangesAsync();
        var rows = (await fixture.CreateJournalService(db).GetJournalAsync(DateTime.Today, DateTime.Today)).Operations.Where(x => x.SourceId == h.Id && x.SourceType == "حواله").ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(20, rows.Sum(x => x.LedgerEntries.Count));
        Assert.All(rows, x => Assert.Equal(2, x.CurrencySummaries.Count));
    }

    [Fact]
    public async Task Optimized_account_procedure_matches_previous_results_for_corrected_and_original_hawalas()
    {
        await using var db = fixture.CreateContext();
        using var bypass = db.BypassSubscriptionEnforcement();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var corrected = await Seed(db, true);
        var original = new Hawala { Number = corrected.Number + 1000000, HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id,
            FromCurrencyId = 1, ToCurrencyId = 1, FromAmount = 500, ToAmount = 500, Status = "Paid", CreatedBy = fixture.UserId };
        db.Hawalas.Add(original); await db.SaveChangesAsync();
        db.LedgerEntries.AddRange(new LedgerEntry { HawalaId = original.Id, AccountId = fixture.SourceAccount.Id, CurrencyId = 1, BadehKar = 500, Description = "original" },
            new LedgerEntry { HawalaId = original.Id, AccountId = fixture.DestinationAccount.Id, CurrencyId = 1, TalabKar = 500, Description = "original" });
        await db.SaveChangesAsync();
        var filters = new[] {
            new AccountOperationsFilterDto { AccountId = fixture.SourceAccount.Id, PageSize = 2 },
            new AccountOperationsFilterDto { AccountId = fixture.SourceAccount.Id, PageSize = 2, PageNumber = 2 },
            new AccountOperationsFilterDto { AccountId = fixture.SourceAccount.Id, SearchTerm = corrected.Number.ToString(), CurrencyCode = "AFN", Direction = "Debit" },
            new AccountOperationsFilterDto { AccountId = fixture.SourceAccount.Id, SearchTerm = original.Number.ToString() }
        };
        async Task Apply(string resource)
        {
            using var stream = typeof(HawalaExchange.Infrastructure.Migrations.OptimizeCorrespondentStatus).Assembly.GetManifestResourceStream("HawalaExchange.Infrastructure.Sql." + resource)!;
            using var reader = new StreamReader(stream);
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(await reader.ReadToEndAsync(), @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
                await db.Database.ExecuteSqlRawAsync(batch);
        }
        var service = fixture.CreateJournalService(db);
        await Apply("HawalaPostingHistory.sql");
        var before = new List<string>();
        foreach (var filter in filters) before.Add(JsonSerializer.Serialize(await service.GetAccountOperationsPageAsync(filter)));
        await Apply("AccountOperationsOptimized.sql");
        for (var i = 0; i < filters.Length; i++)
            Assert.Equal(before[i], JsonSerializer.Serialize(await service.GetAccountOperationsPageAsync(filters[i])));
        // Rollback also restores the procedure definition and all test-only financial data.
    }

    private async Task<Hawala> Seed(HawalaExchange.Infrastructure.Data.ApplicationDbContext db, bool multipleCurrencies = false)
    {
        var h = new Hawala { Number = 96000000 + Random.Shared.Next(100000), HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id, FromCurrencyId = 1, ToCurrencyId = 1, FromAmount = 15000, ToAmount = 15000, Status = "Paid", CreatedBy = fixture.UserId, CreatedAt = DateTime.Today.AddDays(-2).ToUniversalTime() };
        db.Hawalas.Add(h); await db.SaveChangesAsync();
        var replacement = new Account { AccountCode = "CORR-" + h.Id, AccountName = "Correction destination", AccountType = "Cash" };
        db.Accounts.Add(replacement); await db.SaveChangesAsync();
        for(int batch = 0; batch < 5; batch++)
        {
            bool reverse = batch % 2 == 1;
            long target = batch < 2 ? fixture.DestinationAccount.Id : replacement.Id;
            var date = batch == 0 ? h.CreatedAt : DateTime.Today.AddDays(-1).AddHours(10).AddMinutes(batch).ToUniversalTime();
            var note = reverse ? "اصلاح حواله آپلود گروهی — برگشت مانده ثبت قبلی" : "ثبت حواله";
            foreach (var currency in multipleCurrencies ? new long[] { 1, 2 } : new long[] { 1 })
            db.LedgerEntries.AddRange(new LedgerEntry { HawalaId = h.Id, AccountId = fixture.SourceAccount.Id, CurrencyId = currency, TalabKar = reverse ? 15000 : 0, BadehKar = reverse ? 0 : 15000, Description = note, CreatedAt = date }, new LedgerEntry { HawalaId = h.Id, AccountId = target, CurrencyId = currency, TalabKar = reverse ? 0 : 15000, BadehKar = reverse ? 15000 : 0, Description = note, CreatedAt = date });
            await db.SaveChangesAsync();
        }
        return h;
    }
}
