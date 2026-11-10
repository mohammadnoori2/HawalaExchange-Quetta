using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class HawalaCancellationWorkflowTests(SqlServerPerformanceFixture fixture)
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public async Task Cancelling_preserves_registered_fees_and_neutralizes_original_accounts_in_each_currency(bool converted, bool commission, bool closed)
    {
        await using var db = fixture.CreateContext();
        using var bypass = db.BypassSubscriptionEnforcement();
        var seed = await Seed(db, converted, commission, closed);
        var service = fixture.CreateService(db);
        var preview = await service.GetCancellationPreviewAsync(seed.Root.Id);
        Assert.Equal(commission, preview.HasCommission);
        Assert.Equal(converted, preview.HasConversion);
        Assert.Equal(closed, preview.IsClosedPeriod);
        foreach (var group in preview.Entries.GroupBy(x => x.CurrencyId))
            Assert.Equal(group.Sum(x => x.BadehKar), group.Sum(x => x.TalabKar));
        if (converted) Assert.Equal(65, Assert.Single(preview.Rates).Rate);
        var before = await db.LedgerEntries.CountAsync();
        var cancel = new CancelHawalaDto { CancelReason = "Cancellation regression", ConfirmationToken = preview.ConfirmationToken, CompensationMode = "Manual" };
        await service.CancelHawalaAsync(seed.Child.Id, cancel);
        Assert.Equal("Cancel", seed.Root.Status);
        Assert.Equal("Cancel", seed.Child.Status);
        if (converted && commission)
        {
            Assert.Equal(before, await db.LedgerEntries.CountAsync());
            var pending = await service.GetCancellationPreviewAsync(seed.Root.Id);
            await service.CompleteCancellationAsync(seed.Root.Id, new() { ConfirmationToken = pending.ConfirmationToken, Manual = true,
                Number = 850001, ReceiverName = "Manual compensation", ReferenceNumber = "MANUAL-1" });
            var compensation = await db.Hawalas.SingleAsync(x => x.CompensatesHawalaId == seed.Root.Id);
            Assert.Equal(1000, compensation.ToAmount);
            Assert.Equal(2, compensation.ToCurrencyId);
            Assert.Equal(65000, compensation.FromAmount);
            Assert.Equal(1, compensation.FromCurrencyId);
            Assert.Equal(65, compensation.ExchangeRate);
            Assert.False(compensation.IsSystemGenerated);
            var count = await db.LedgerEntries.CountAsync();
            await service.CompleteCancellationAsync(seed.Root.Id, new() { ConfirmationToken = pending.ConfirmationToken, Manual = true,
                Number = 850001, ReceiverName = "Manual compensation" });
            Assert.Equal(count, await db.LedgerEntries.CountAsync());
        }
        var ids = new[] { seed.SourceAccount.Id, seed.TargetAccount.Id, seed.PendingAccount.Id };
        var balances = await db.LedgerEntries.Where(x => ids.Contains(x.AccountId)).ToListAsync();
        foreach (var group in balances.GroupBy(x => (x.AccountId, x.CurrencyId)))
        {
            var expected = commission && group.Key.AccountId == seed.SourceAccount.Id && group.Key.CurrencyId == 2 ? 4m : 0m;
            Assert.Equal(expected, group.Sum(x => x.BadehKar - x.TalabKar));
        }
        if (commission)
        {
            Assert.True(await db.CorrespondentCommissionBatchItems.Where(x => x.HawalaId == seed.Root.Id).AllAsync(x => x.IsActive));
            var next = await fixture.CreateCommissionService(db).PreviewAsync(new() { CorrespondentId = seed.Root.CorrespondentId!.Value,
                CommissionScope = "Incoming", HawalaType = "HawalaReceive", AllUncalculated = true, CommissionPerLakhAfn = 400 });
            Assert.Empty(next.Items); // No automatic deduction of preserved commission.
        }
        if (closed) Assert.Equal(65000, (await db.CorrespondentAccountPeriodBalances.SingleAsync(x => x.PeriodId == seed.PeriodId)).BadehKar);
        if (converted)
        {
            var conversionId = await db.CorrespondentSettlementConversionHawalas.Where(x => x.HawalaId == seed.Root.Id).Select(x => x.ConversionId).SingleAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateSettlementService(db).ReverseAsync(seed.Root.CorrespondentId!.Value, conversionId, "No double reversal"));
        }
    }

    [Fact]
    public async Task Stale_preview_is_rejected_and_automatic_compensation_is_idempotent_and_tenant_scoped()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var seed = await Seed(db, true, true, false);
        var service = fixture.CreateService(db);
        var preview = await service.GetCancellationPreviewAsync(seed.Root.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelHawalaAsync(seed.Root.Id,
            new() { CancelReason = "Test", ConfirmationToken = "stale", CompensationMode = "Automatic" }));
        Assert.NotEqual("Cancel", seed.Root.Status);
        await service.CancelHawalaAsync(seed.Root.Id, new() { CancelReason = "Test", ConfirmationToken = preview.ConfirmationToken, CompensationMode = "Automatic" });
        var count = await db.LedgerEntries.CountAsync();
        await service.CancelHawalaAsync(seed.Root.Id, new() { CancelReason = "Test", ConfirmationToken = preview.ConfirmationToken, CompensationMode = "Automatic" });
        Assert.Equal(count, await db.LedgerEntries.CountAsync());
        Assert.Single(await db.Hawalas.Where(x => x.CompensatesHawalaId == seed.Root.Id).ToListAsync());
        using (db.UseTenantScope(999999))
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetCancellationPreviewAsync(seed.Root.Id));
    }

    [Fact]
    public async Task Both_converted_accounts_keep_their_own_historical_rates_and_balanced_clearing()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var seed = await Seed(db, true, true, false);
        await fixture.CreateSettlementService(db).ConvertHawalasAsync(new() { CorrespondentId = seed.Child.CorrespondentId!.Value,
            HawalaIds = [seed.Child.Id], HawalaRates = [new() { HawalaId = seed.Child.Id, SourceCurrencyId = 1, Rate = 70 }] });
        var service = fixture.CreateService(db);
        var preview = await service.GetCancellationPreviewAsync(seed.Root.Id);
        Assert.Equal(new[] { 65m, 70m }, preview.Rates.Select(x => x.Rate).Order().ToArray());
        Assert.Equal(1000, preview.UsdAmount);
        foreach (var currency in preview.Entries.GroupBy(x => x.CurrencyId))
            Assert.Equal(currency.Sum(x => x.BadehKar), currency.Sum(x => x.TalabKar));
        await service.CancelHawalaAsync(seed.Root.Id, new() { CancelReason = "Different historical rates", ConfirmationToken = preview.ConfirmationToken, CompensationMode = "Automatic" });
        var compensation = await db.Hawalas.SingleAsync(x => x.CompensatesHawalaId == seed.Root.Id);
        Assert.Null(compensation.ExchangeRate); // Multiple historical legs are not a single new FX rate.
        Assert.True(compensation.IsSystemGenerated);
        Assert.Equal(0, await db.LedgerEntries.Where(x => x.AccountId == seed.TargetAccount.Id).SumAsync(x => x.BadehKar - x.TalabKar));
        Assert.Equal(4, await db.LedgerEntries.Where(x => x.AccountId == seed.SourceAccount.Id).SumAsync(x => x.BadehKar - x.TalabKar));
    }

    [Fact]
    public async Task Concurrent_cancellation_requests_create_exactly_one_compensation()
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var seed = await Seed(db, true, true, false);
        var preview = await fixture.CreateService(db).GetCancellationPreviewAsync(seed.Root.Id);
        async Task Cancel()
        {
            await using var worker = fixture.CreateContext(); using var access = worker.BypassSubscriptionEnforcement();
            await fixture.CreateService(worker).CancelHawalaAsync(seed.Root.Id, new() { CancelReason = "Concurrent test",
                CompensationMode = "Automatic", ConfirmationToken = preview.ConfirmationToken });
        }
        await Task.WhenAll(Cancel(), Cancel());
        Assert.Equal(1, await db.Hawalas.CountAsync(x => x.CompensatesHawalaId == seed.Root.Id));
        Assert.Equal(4, await db.LedgerEntries.Where(x => x.AccountId == seed.SourceAccount.Id).SumAsync(x => x.BadehKar - x.TalabKar));
    }

    private async Task<Seeded> Seed(ApplicationDbContext db, bool converted, bool commission, bool closed)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var source = new Correspondent { Code = "C-S-" + suffix, Name = "Quetta " + suffix, SettlementCurrencyId = 2, CommissionMethod = "PeriodicPerLakh" };
        var target = new Correspondent { Code = "C-D-" + suffix, Name = "Barchi " + suffix, SettlementCurrencyId = 2, CommissionMethod = "PeriodicPerLakh" };
        db.AddRange(source, target); await db.SaveChangesAsync();
        var sa = new Account { AccountCode = source.Code, AccountName = source.Name, AccountType = "Correspondent", CorrespondentId = source.Id };
        var ta = new Account { AccountCode = target.Code, AccountName = target.Name, AccountType = "Correspondent", CorrespondentId = target.Id };
        var pa = new Account { AccountCode = "C-P-" + suffix, AccountName = "Pending", AccountType = "PendingHawala" };
        var income = new Account { AccountCode = "C-I-" + suffix, AccountName = "Fee", AccountType = "Income" };
        db.AddRange(sa, ta, pa, income); await db.SaveChangesAsync();
        var root = new Hawala { Number = 700001, HawalaType = "HawalaReceive", CorrespondentId = source.Id, PaymentLocationId = fixture.RemoteLocation.Id,
            FromCurrencyId = 1, ToCurrencyId = 1, FromAmount = 65000, ToAmount = 65000, Status = "Paid", CreatedBy = fixture.UserId,
            CreatedAt = DateTime.UtcNow.AddDays(-2), SenderName = "Sender", ReceiverName = "Receiver" };
        db.Add(root); await db.SaveChangesAsync();
        var child = new Hawala { Number = 700002, HawalaType = "HawalaSend", CorrespondentId = target.Id, SourceHawalaId = root.Id, IsSystemGenerated = true,
            FromCurrencyId = 1, ToCurrencyId = 1, FromAmount = 65000, ToAmount = 65000, Status = "Paid", CreatedBy = fixture.UserId,
            CreatedAt = root.CreatedAt, SenderName = "Sender", ReceiverName = "Receiver", PaymentLocationId = fixture.RemoteLocation.Id };
        db.Add(child); await db.SaveChangesAsync();
        db.LedgerEntries.AddRange(
            new LedgerEntry { HawalaId = root.Id, AccountId = sa.Id, CurrencyId = 1, BadehKar = 65000, Description = "Original receipt" },
            new LedgerEntry { HawalaId = root.Id, AccountId = pa.Id, CurrencyId = 1, TalabKar = 65000, Description = "Original pending" },
            new LedgerEntry { HawalaId = child.Id, AccountId = pa.Id, CurrencyId = 1, BadehKar = 65000, Description = "Original release" },
            new LedgerEntry { HawalaId = child.Id, AccountId = ta.Id, CurrencyId = 1, TalabKar = 65000, Description = "Original forwarded" });
        await db.SaveChangesAsync();
        if (converted) await fixture.CreateSettlementService(db).ConvertHawalasAsync(new() { CorrespondentId = source.Id, HawalaIds = [root.Id],
            HawalaRates = [new() { HawalaId = root.Id, SourceCurrencyId = 1, Rate = 65 }] });
        if (commission)
        {
            var transaction = new Transaction { TransactionNo = "C-F-" + suffix, TransactionType = "PeriodicCorrespondentCommission", BranchId = 1,
                Status = "Paid", CreatedBy = fixture.UserId };
            db.Add(transaction); await db.SaveChangesAsync();
            var batch = new CorrespondentCommissionBatch { CorrespondentId = source.Id, CommissionScope = "Incoming", AccountingVersion = 3,
                PeriodFrom = root.CreatedAt.Date, PeriodTo = root.CreatedAt.Date, CommissionPerLakhAfn = 400, TotalBaseAfn = 1000,
                TotalCommissionUsd = 4, Status = "Posted", PostingTransactionId = transaction.Id, CreatedBy = fixture.UserId,
                Items = [new() { HawalaId = root.Id, CommissionScope = "Incoming", SourceCurrencyId = 1, SourceAmount = 65000,
                    SourceToAfnRate = 65, AfnEquivalent = 1000, CommissionAfn = 4, PerLakhRate = 400, IsActive = true }] };
            db.Add(batch);
            db.LedgerEntries.AddRange(new LedgerEntry { TransactionId = transaction.Id, AccountId = sa.Id, CurrencyId = 2, BadehKar = 4, Description = "کمیشن" },
                new LedgerEntry { TransactionId = transaction.Id, AccountId = income.Id, CurrencyId = 2, TalabKar = 4, Description = "کمیشن" });
            await db.SaveChangesAsync();
        }
        long? periodId = null;
        if (closed)
        {
            var period = new CorrespondentAccountPeriod { CorrespondentId = source.Id, PeriodNumber = 1, PeriodFrom = root.CreatedAt.AddDays(-1),
                PeriodTo = DateTime.UtcNow.AddDays(-1), ClosedAt = DateTime.UtcNow.AddDays(-1), ClosedBy = fixture.UserId,
                Balances = [new() { CurrencyId = 1, BadehKar = 65000 }], Hawalas = [new() { HawalaId = root.Id }] };
            db.Add(period); await db.SaveChangesAsync(); periodId = period.Id;
        }
        return new(root, child, sa, ta, pa, periodId);
    }
    private sealed record Seeded(Hawala Root, Hawala Child, Account SourceAccount, Account TargetAccount, Account PendingAccount, long? PeriodId);
}
