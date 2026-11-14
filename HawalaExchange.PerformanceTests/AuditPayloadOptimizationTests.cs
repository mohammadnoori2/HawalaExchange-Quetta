using System.Text.Json;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class AuditPayloadOptimizationTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Repeated_currency_cost_rebuilds_preserve_balances_and_existing_audits_without_duplicate_payloads()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        await using var tx = await db.Database.BeginTransactionAsync();
        var cash = await db.Accounts.FirstAsync(x => x.AccountType == "Cash");
        var day = new DateTime(2055, 1, 1);
        db.MoneyExchangeOperations.AddRange(Enumerable.Range(1, 40).Select(i => new MoneyExchangeOperation {
            FromAccountId = cash.Id, ToAccountId = cash.Id, FromCurrencyId = 1, ToCurrencyId = 2,
            FromAmount = 100 + i, ToAmount = 2, ExchangeRate = (100 + i) / 2m,
            ExchangeDate = day.AddMinutes(i), CreatedAt = day.AddMinutes(i), CreatedBy = fixture.UserId }));
        await db.SaveChangesAsync();
        var service = new CurrencyCostService(db);
        await service.RebuildAsync();
        var originalAudits = await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OldValue, x.NewValue, x.Action, x.TableName, x.RecordId }).ToListAsync();
        var balances = await Balances(); var ledgerCount = await db.LedgerEntries.CountAsync();
        db.ChangeTracker.Clear(); // New requests reload SQL decimal scales and datetime2 values.
        await service.RebuildAsync(); await service.RebuildAsync();
        Assert.Equal(ledgerCount, await db.LedgerEntries.CountAsync());
        Assert.Equal(balances, await Balances());
        Assert.Equal(originalAudits, await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OldValue, x.NewValue, x.Action, x.TableName, x.RecordId }).ToListAsync());

        async Task<string> Balances() => JsonSerializer.Serialize(await db.LedgerEntries.AsNoTracking()
            .GroupBy(x => new { x.AccountId, x.CurrencyId }).Select(g => new {
                g.Key.AccountId, g.Key.CurrencyId, Debit = g.Sum(x => x.BadehKar), Credit = g.Sum(x => x.TalabKar) })
            .OrderBy(x => x.AccountId).ThenBy(x => x.CurrencyId).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_identical_derived_rows_are_omitted_and_real_changes_and_duplicate_counts_are_audited(bool sync)
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        await using var tx = await db.Database.BeginTransactionAsync();
        var cash = await db.Accounts.FirstAsync(x => x.AccountType == "Cash");
        var exchange = new MoneyExchangeOperation { FromAccountId = cash.Id, ToAccountId = cash.Id,
            FromCurrencyId = 1, ToCurrencyId = 2, FromAmount = 100, ToAmount = 2, ExchangeRate = 50,
            ExchangeDate = new(2056, 1, 1), CreatedBy = fixture.UserId };
        db.MoneyExchangeOperations.Add(exchange); await db.SaveChangesAsync();
        LedgerEntry Row(string description, decimal amount = 10) => new() { MoneyExchangeOperationId = exchange.Id,
            AccountId = cash.Id, CurrencyId = 1, BadehKar = amount, Description = description, CreatedAt = exchange.ExchangeDate };
        var unchanged = Row("UNCHANGED"); var changed = Row("CHANGED-AMOUNT"); var duplicate = Row("DUPLICATE");
        db.LedgerEntries.AddRange(unchanged, changed, duplicate); await db.SaveChangesAsync();
        var process = db.GetCurrentAuditProcessId(); var audit = await db.AuditLogs.SingleAsync(x => x.ProcessId == process);
        var oldJson = audit.OldValue;
        db.LedgerEntries.RemoveRange(unchanged, changed, duplicate);
        var replacement = Row("UNCHANGED"); var changedReplacement = Row("CHANGED-AMOUNT", 20);
        db.LedgerEntries.AddRange(replacement, changedReplacement, Row("DUPLICATE"), Row("DUPLICATE"));
        // A non-exchange ledger row must never disappear from audit capture.
        var ordinary = Row("ORDINARY"); ordinary.MoneyExchangeOperationId = null; db.LedgerEntries.Add(ordinary);
        if (sync) db.SaveChanges(); else await db.SaveChangesAsync();
        using var before = JsonDocument.Parse(audit.OldValue!); using var after = JsonDocument.Parse(audit.NewValue!);
        // The CREATE already stored display metadata on the old side; it must not acquire
        // a financial deletion snapshot during an unchanged rebuild.
        Assert.DoesNotContain(before.RootElement.EnumerateObject(), x => x.Name == $"LedgerEntries\u001f{unchanged.Id}\u001fBadehKar");
        Assert.Contains(before.RootElement.EnumerateObject(), x => x.Name == $"LedgerEntries\u001f{changed.Id}\u001fBadehKar" && x.Value.GetDecimal() == 10);
        Assert.Contains(after.RootElement.EnumerateObject(), x => x.Name == $"LedgerEntries\u001f{changedReplacement.Id}\u001fBadehKar" && x.Value.GetDecimal() == 20);
        Assert.DoesNotContain(after.RootElement.EnumerateObject(), x => x.Name.StartsWith($"LedgerEntries\u001f{replacement.Id}\u001f"));
        Assert.Contains(after.RootElement.EnumerateObject(), x => x.Name.StartsWith($"LedgerEntries\u001f{ordinary.Id}\u001f"));
        Assert.Equal(2, after.RootElement.EnumerateObject().Count(x => x.Name.EndsWith("\u001fDescription") && x.Value.GetString() == "DUPLICATE"));
        Assert.Equal(3, await db.LedgerEntries.CountAsync(x => x.MoneyExchangeOperationId == exchange.Id && x.Description != "UNCHANGED"));
        Assert.NotEqual(oldJson, audit.OldValue);
    }
}
