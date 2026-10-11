using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class ForwardedHawalaMigrationTests
{
    [Fact]
    public async Task Upgrade_completes_only_already_accounted_forwarded_sends_without_new_ledger_entries()
    {
        var database = new SqlServerPerformanceFixture();
        try
        {
            await database.InitializeDatabaseAsync("20261009053125_AddDurableHawalaImportQueue");
            await using var context = database.CreateContext();
            using var bypass = context.BypassSubscriptionEnforcement();
            var paidAt = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
            Hawala Source(long number, string status) => new()
            {
                Number = number, HawalaType = "HawalaReceive", Status = status,
                CorrespondentId = database.SourceCorrespondent.Id, FromCurrencyId = 2, ToCurrencyId = 2,
                FromAmount = 100, ToAmount = 100, CreatedAt = paidAt, CreatedBy = database.UserId,
                PaidAt = status == "Paid" ? paidAt : null, PaidBy = status == "Paid" ? database.UserId : null
            };
            var paidSource = Source(98700001, "Paid");
            var pendingSource = Source(98700002, "Pending");
            context.Hawalas.AddRange(paidSource, pendingSource);
            await context.SaveChangesAsync();
            Hawala Sent(long number, Hawala source, string status = "Pending", bool generated = true) => new()
            {
                Number = number, HawalaType = "HawalaSend", Status = status,
                CorrespondentId = database.DestinationCorrespondent.Id, SourceHawalaId = source.Id,
                IsSystemGenerated = generated, PaidFromAccountId = database.DestinationAccount.Id,
                FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 100, ToAmount = 100,
                CreatedAt = paidAt, CreatedBy = database.UserId
            };
            var complete = Sent(98700003, paidSource);
            // SourceHawalaId is unique. Create an independent source for each linked send.
            var sources = Enumerable.Range(4, 3).Select(i => Source(98700000 + i, "Paid")).ToArray();
            context.Hawalas.AddRange(sources);
            await context.SaveChangesAsync();
            var cancelled = Sent(98700007, sources[0], "Cancel");
            var manual = Sent(98700008, sources[1], generated: false);
            var unaccounted = Sent(98700009, sources[2]);
            var unpaidSourceSend = Sent(98700010, pendingSource);
            context.Hawalas.AddRange(complete, cancelled, manual, unaccounted, unpaidSourceSend);
            await context.SaveChangesAsync();
            foreach (var send in new[] { complete, cancelled, manual, unpaidSourceSend })
                context.LedgerEntries.Add(new LedgerEntry
                {
                    HawalaId = send.Id, AccountId = database.DestinationAccount.Id, CurrencyId = 2,
                    TalabKar = 100, Description = "Existing forwarding entry", CreatedAt = paidAt
                });
            await context.SaveChangesAsync();
            var ledgerBefore = await context.LedgerEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AccountId, x.HawalaId, x.CurrencyId, x.TalabKar, x.BadehKar }).ToListAsync();
            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();
            var actual = await context.Hawalas.SingleAsync(x => x.Id == complete.Id);
            Assert.Equal("Paid", actual.Status);
            Assert.Equal(paidAt, actual.PaidAt);
            Assert.Equal(database.UserId, actual.PaidBy);
            Assert.Equal("Cancel", (await context.Hawalas.SingleAsync(x => x.Id == cancelled.Id)).Status);
            foreach (var unchanged in new[] { manual, unaccounted, unpaidSourceSend })
                Assert.Equal("Pending", (await context.Hawalas.SingleAsync(x => x.Id == unchanged.Id)).Status);
            Assert.Equal(ledgerBefore, await context.LedgerEntries.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AccountId, x.HawalaId, x.CurrencyId, x.TalabKar, x.BadehKar }).ToListAsync());
        }
        finally { await database.DisposeAsync(); }
    }
}
