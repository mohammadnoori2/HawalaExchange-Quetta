using System.Text.Json;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

// Corrections append journals and replacement snapshots; closed-period journals are never rewritten.
internal sealed class ImportCommissionCorrectionService(ApplicationDbContext context)
{
    internal async Task<List<CorrespondentCommissionBatch>> ReverseAsync(long[] ids, CancellationToken token = default)
    {
        if (context.Database.CurrentTransaction == null)
            throw new InvalidOperationException("اصلاح کمیشن باید در تراکنش حواله انجام شود.");
        var batchIds = await context.CorrespondentCommissionBatchItems.AsNoTracking()
            .Where(x => ids.Contains(x.HawalaId) && x.IsActive && x.Batch.Status == "Posted")
            .Select(x => x.BatchId).Distinct().ToArrayAsync(token);
        var batchOwners = await context.CorrespondentCommissionBatches.AsNoTracking()
            .Where(x => batchIds.Contains(x.Id)).Select(x => x.CorrespondentId).ToListAsync(token);
        var hawalaOwners = await context.Hawalas.AsNoTracking().Where(x => ids.Contains(x.Id) && x.CorrespondentId != null)
            .Select(x => x.CorrespondentId!.Value).Distinct().ToListAsync(token);
        foreach (var owner in batchOwners.Concat(hawalaOwners).Distinct().OrderBy(x => x))
            await CorrespondentCommissionPeriodLock.AcquireAsync(context, owner, token);
        // A calculation may have completed while we were waiting for its lock.
        batchIds = await context.CorrespondentCommissionBatchItems.AsNoTracking()
            .Where(x => ids.Contains(x.HawalaId) && x.IsActive && x.Batch.Status == "Posted")
            .Select(x => x.BatchId).Distinct().ToArrayAsync(token);
        var originals = await context.CorrespondentCommissionBatches.AsNoTracking().AsSplitQuery()
            .Include(x => x.Items).ThenInclude(x => x.Hawala)
            .Where(x => batchIds.Contains(x.Id) && x.Status == "Posted").OrderBy(x => x.Id).ToListAsync(token);
        var service = new CorrespondentCommissionService(context);
        foreach (var batch in originals)
            await service.ReverseAsync(batch.Id, "اصلاح حواله‌های آپلود گروهی؛ سهم سایر حواله‌ها در سند جایگزین حفظ می‌شود.", token);
        return originals;
    }

    internal async Task RepostAsync(List<CorrespondentCommissionBatch> originals, long[] affectedIds, bool deleting, CancellationToken token = default)
    {
        if (originals.Count == 0) return;
        var affectedSet = affectedIds.ToHashSet();
        var currencies = await context.Currencies.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, token);
        var affected = await context.Hawalas.AsNoTracking().Include(x => x.SourceHawala).Include(x => x.PaymentLocation)
            .Where(x => affectedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
        foreach (var original in originals)
        {
            var groups = new Dictionary<long, List<CorrespondentCommissionBatchItem>>();
            foreach (var snapshot in original.Items.Where(x => x.IsActive))
            {
                var owner = original.CorrespondentId;
                if (affectedSet.Contains(snapshot.HawalaId) && deleting) continue;
                var item = Copy(snapshot);
                if (affected.TryGetValue(item.HawalaId, out var hawala))
                {
                    if (hawala.Status == "Cancel") continue;
                    if (original.CommissionScope is "Forwarding" or "Origin")
                    {
                        if (hawala.SourceHawalaId == null && !await context.Hawalas.AnyAsync(x => x.SourceHawalaId == hawala.Id && x.Status != "Cancel", token)) continue;
                        owner = hawala.SourceHawala?.CorrespondentId ?? hawala.CorrespondentId ?? owner;
                    }
                    else owner = hawala.CorrespondentId ?? owner;
                    var outgoing = hawala.HawalaType == "HawalaSend";
                    var currencyId = outgoing ? hawala.ToCurrencyId : hawala.FromCurrencyId;
                    if (currencies.GetValueOrDefault(currencyId) is not ("AFN" or "USD"))
                        throw new InvalidOperationException("اصلاح کمیشن فعلاً فقط برای AFN و USD امکان دارد.");
                    var rate = item.SourceToAfnRate;
                    if (currencies[currencyId] == "USD") rate = 1;
                    else if (original.CommissionScope != "Destination" && (currencyId != item.SourceCurrencyId || rate <= 0 || (rate == 1 && currencies[item.SourceCurrencyId] == "USD")))
                    {
                        var date = item.ValuationDate ?? hawala.SourceHawala?.CreatedAt.ToLocalTime().Date ?? hawala.CreatedAt.ToLocalTime().Date;
                        rate = await context.CorrespondentDailyCommissionRates.Where(x => x.CorrespondentId == owner && x.RateDate == date)
                            .Select(x => (decimal?)x.UsdToAfnRate).SingleOrDefaultAsync(token)
                            ?? throw new InvalidOperationException("نرخ تاریخی نمایندگی برای ارز اصلاح‌شده ثبت نشده است.");
                    }
                    item.SourceCurrencyId = currencyId;
                    item.SourceAmount = (outgoing ? hawala.ToAmount ?? hawala.FromAmount : hawala.FromAmount) * (snapshot.SourceAmount < 0 ? -1 : 1);
                    item.SourceToAfnRate = original.CommissionScope == "Destination" && currencies[currencyId] == "AFN" ? 0 : rate;
                    if ((hawala.PaymentLocationId != snapshot.PaymentLocationId && original.CommissionScope is "Forwarding" or "Destination") ||
                        (currencyId != snapshot.SourceCurrencyId && original.CommissionScope == "Destination"))
                    {
                        item.PerLakhRate = original.Items.Where(x => x.PaymentLocationId == hawala.PaymentLocationId &&
                                (original.CommissionScope != "Destination" || x.SourceCurrencyId == currencyId))
                            .Select(x => x.PerLakhRate).FirstOrDefault()
                            ?? throw new InvalidOperationException("نرخ تاریخی کمیشن محل پرداخت جدید در این محاسبه موجود نیست؛ ابتدا کمیشن مربوط را برگشت دهید.");
                    }
                    item.PaymentLocationId = hawala.PaymentLocationId;
                    item.PaymentLocationName = hawala.PaymentLocation?.Name;
                    var perLakh = item.PerLakhRate ?? original.CommissionPerLakhAfn;
                    if (original.CommissionScope == "Destination")
                    {
                        item.AfnEquivalent = 0;
                        item.CommissionAfn = decimal.Round(item.SourceAmount / 100000m * perLakh, 4, MidpointRounding.AwayFromZero);
                    }
                    else if (original.AccountingVersion >= 2 || !outgoing)
                    {
                        if (rate <= 0) throw new InvalidOperationException("نرخ تاریخی کمیشن معتبر نیست.");
                        item.AfnEquivalent = decimal.Round(item.SourceAmount / rate, 4, MidpointRounding.AwayFromZero);
                        item.CommissionAfn = decimal.Round(item.AfnEquivalent / 100000m * perLakh, 4, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        item.CommissionAfn = decimal.Round(item.SourceAmount / 100000m * perLakh, 2, MidpointRounding.AwayFromZero);
                        item.AfnEquivalent = decimal.Round(item.CommissionAfn / rate, 2, MidpointRounding.AwayFromZero);
                    }
                }
                if (!groups.TryGetValue(owner, out var items)) groups[owner] = items = [];
                items.Add(item);
            }
            foreach (var (owner, items) in groups)
            {
                await CorrespondentCommissionPeriodLock.AcquireAsync(context, owner, token);
                var outgoing = original.Items.First().Hawala.HawalaType == "HawalaSend";
                var replacement = new CorrespondentCommissionBatch
                {
                    CorrespondentId = owner, CommissionScope = original.CommissionScope, AccountingVersion = original.AccountingVersion,
                    PeriodFrom = original.PeriodFrom, PeriodTo = original.PeriodTo, CommissionPerLakhAfn = original.CommissionPerLakhAfn,
                    UsdToAfnRate = original.UsdToAfnRate, CreatedBy = context.RequireCurrentUserId(), Items = items,
                    TotalBaseAfn = items.Sum(x => x.AfnEquivalent)
                };
                (replacement.TotalCommissionAfn, replacement.TotalCommissionUsd) = Totals(original, items, currencies);
                // Stored item fees are allocated whole units, not the raw fee. Reconstruct
                // from historical amount/rate before rounding the remaining group again.
                AllocateWholeFees(original, items);
                var posting = new Transaction
                {
                    TransactionNo = "PCI-" + Guid.NewGuid().ToString("N"),
                    TransactionType = original.CommissionScope == "Forwarding" ? "PeriodicForwardingCommission" : outgoing ? "PeriodicOutgoingCommission" : "PeriodicCorrespondentCommission",
                    BranchId = await context.GetDefaultBranchIdAsync(token), Status = "Paid", CreatedBy = context.RequireCurrentUserId(),
                    Remarks = $"اصلاح کمیشن {original.Id} پس از اصلاح آپلود گروهی؛ نرخ‌های تاریخی حفظ شدند."
                };
                context.Transactions.Add(posting);
                await context.SaveChangesAsync(token);
                replacement.PostingTransactionId = posting.Id;
                context.CorrespondentCommissionBatches.Add(replacement);
                await context.SaveChangesAsync(token);
                if (original.CommissionScope == "Destination" || original.CommissionScope == "Standard" && outgoing)
                    await new CorrespondentCommissionService(context).RebuildOutgoingLedgerAsync(replacement, token);
                else
                {
                    var code = original.CommissionScope == "Incoming" ? "SYS-COMMISSION-INCOMING" : original.CommissionScope is "Forwarding" or "Origin" ? "SYS-COMMISSION-FORWARDING" : "4002";
                    var incomeId = await context.Accounts.Where(x => x.AccountCode == code).Select(x => (long?)x.Id).SingleOrDefaultAsync(token);
                    if (!incomeId.HasValue)
                    {
                        // Older journals use their original income account code.
                        incomeId = await context.LedgerEntries.Where(x => x.TransactionId == original.PostingTransactionId && x.Account.AccountType == "Income")
                            .Select(x => (long?)x.AccountId).FirstOrDefaultAsync(token);
                    }
                    var accountId = await context.Accounts.Where(x => x.CorrespondentId == owner && !x.IsArchived).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(token);
                    var usdId = currencies.Single(x => x.Value == "USD").Key;
                    if (!incomeId.HasValue) throw new InvalidOperationException("حساب درآمد کمیشن یافت نشد.");
                    AddEntry(posting.Id, accountId, usdId, 0, replacement.TotalCommissionUsd);
                    AddEntry(posting.Id, incomeId.Value, usdId, replacement.TotalCommissionUsd, 0);
                }
                context.AuditLogs.Add(new AuditLog { UserId = context.RequireCurrentUserId(), Action = "CORRECT_IMPORT_COMMISSION", TableName = "CorrespondentCommissionBatches", RecordId = replacement.Id,
                    NewValue = JsonSerializer.Serialize(new { OriginalBatchId = original.Id, replacement.TotalCommissionAfn, replacement.TotalCommissionUsd }) });
                await context.SaveChangesAsync(token);
            }
        }
    }

    internal Task ReverseHawalaLedgerAsync(long[] ids, CancellationToken token = default) => context.Database.ExecuteSqlInterpolatedAsync($"EXEC dbo.usp_ReverseImportedHawalaLedger_v1 @TenantId={context.CurrentTenantId}, @UserId={context.RequireCurrentUserId()}, @HawalaIds={JsonSerializer.Serialize(ids)}", token);
    internal static (decimal Afn, decimal Usd) Totals(CorrespondentCommissionBatch batch, IReadOnlyCollection<CorrespondentCommissionBatchItem> items, IReadOnlyDictionary<long, string> currencies)
    {
        if (batch.CommissionScope == "Destination" || batch.CommissionScope == "Standard" && batch.Items.First().Hawala.HawalaType == "HawalaSend")
            return (Round(items.Where(x => currencies[x.SourceCurrencyId] == "AFN").Sum(x => RawFee(batch, x))), Round(items.Where(x => currencies[x.SourceCurrencyId] == "USD").Sum(x => RawFee(batch, x))));
        if (batch.CommissionScope == "Standard") return (0, Round(items.Sum(x => x.AfnEquivalent) / 100000m * batch.CommissionPerLakhAfn));
        var dates = batch.Items.ToDictionary(x => x.HawalaId, x => x.Hawala.CreatedAt.ToLocalTime().Date);
        return (0, batch.CommissionScope == "Forwarding"
            ? items.GroupBy(x => dates[x.HawalaId]).Sum(day => Round(day.Sum(x => RawFee(batch, x))))
            : Round(items.Sum(x => RawFee(batch, x))));
    }
    private static decimal RawFee(CorrespondentCommissionBatch batch, CorrespondentCommissionBatchItem item)
    {
        var source = batch.CommissionScope == "Destination" || batch.CommissionScope == "Standard" && batch.Items.First().Hawala.HawalaType == "HawalaSend"
            ? item.SourceAmount : item.AfnEquivalent;
        if (batch.CommissionScope is "Incoming" or "Forwarding" && item.SourceToAfnRate > 0)
            source = decimal.Round(item.SourceAmount / item.SourceToAfnRate, 8, MidpointRounding.AwayFromZero);
        return source / 100000m * (item.PerLakhRate ?? batch.CommissionPerLakhAfn);
    }
    private static void AllocateWholeFees(CorrespondentCommissionBatch original, List<CorrespondentCommissionBatchItem> items)
    {
        var dates = original.Items.ToDictionary(x => x.HawalaId, x => x.Hawala.CreatedAt.ToLocalTime().Date);
        var outgoing = original.CommissionScope == "Destination" || original.CommissionScope == "Standard" && original.Items.First().Hawala.HawalaType == "HawalaSend";
        foreach (var group in items.GroupBy(x => (Date: original.CommissionScope == "Forwarding" ? dates[x.HawalaId] : DateTime.MinValue,
                     Currency: outgoing ? x.SourceCurrencyId : 0)))
        {
            var ordered = group.Select(x => new { Item = x, Raw = RawFee(original, x) })
                .OrderByDescending(x => x.Raw - decimal.Floor(x.Raw)).ThenBy(x => x.Item.HawalaId).ToList();
            var extra = Round(ordered.Sum(x => x.Raw)) - ordered.Sum(x => decimal.Floor(x.Raw));
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].Item.CommissionAfn = decimal.Floor(ordered[i].Raw) + (i < extra ? 1 : 0);
        }
    }
    private static decimal Round(decimal amount) => decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
    private void AddEntry(long transaction, long account, long currency, decimal credit, decimal debit) => context.LedgerEntries.Add(new LedgerEntry { TransactionId = transaction, AccountId = account, CurrencyId = currency, TalabKar = Math.Max(credit - debit, 0), BadehKar = Math.Max(debit - credit, 0), Description = "اصلاح کمیشن آپلود گروهی" });
    private static CorrespondentCommissionBatchItem Copy(CorrespondentCommissionBatchItem x) => new() { HawalaId = x.HawalaId, CommissionScope = x.CommissionScope, ValuationDate = x.ValuationDate, SourceCurrencyId = x.SourceCurrencyId, SourceAmount = x.SourceAmount, SourceToAfnRate = x.SourceToAfnRate, AfnEquivalent = x.AfnEquivalent, CommissionAfn = x.CommissionAfn, PaymentLocationId = x.PaymentLocationId, PaymentLocationName = x.PaymentLocationName, PerLakhRate = x.PerLakhRate };
}
