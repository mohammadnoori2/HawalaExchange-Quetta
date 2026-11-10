using System.Data;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    /// <summary>Changes the forwarding destination without detaching or rebuilding the source settlement.</summary>
    public async Task<HawalaDto> ChangePaymentLocationAsync(long id, long paymentLocationId)
    {
        await using var transaction=_context.Database.CurrentTransaction is null
            ?await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable):null;
        var savepoint=transaction is null?"location_"+Guid.NewGuid().ToString("N")[..20]:null;
        if(savepoint is not null)await _context.Database.CurrentTransaction!.CreateSavepointAsync(savepoint);
        try
        {
            await _context.EnsureCurrentTenantWriteAccessAsync();
            var requested=await _context.Hawalas.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && !x.IsDeleted)
                ??throw new KeyNotFoundException("حواله یافت نشد.");
            var rootId=requested.IsSystemGenerated && requested.SourceHawalaId.HasValue?requested.SourceHawalaId.Value:requested.Id;
            await DatabaseLocks.AcquireAsync(_context,$"HawalaPaymentLocation:{_context.CurrentTenantId}:{rootId}");
            var root=await _context.Hawalas.SingleAsync(x=>x.Id==rootId && !x.IsDeleted);
            await _context.Entry(root).ReloadAsync();
            var sent=await _context.Hawalas.SingleOrDefaultAsync(x=>x.SourceHawalaId==rootId && !x.IsDeleted)
                ??throw new InvalidOperationException("این حواله، حواله ارسالی وابسته برای اصلاح مقصد ندارد.");
            await _context.Entry(sent).ReloadAsync();
            if(root.HawalaType!="HawalaReceive" || root.Status!="Paid" || sent.HawalaType!="HawalaSend" || !sent.IsSystemGenerated ||
               sent.Status=="Cancel" || root.CompensatesHawalaId.HasValue || sent.CompensatesHawalaId.HasValue ||
               root.CancellationAccountingMode is not null || sent.CancellationAccountingMode is not null)
                throw new InvalidOperationException("اصلاح مقصد فقط برای حواله دریافتی ارجاع‌شده و حواله ارسالی فعال آن مجاز است.");
            var newAccountId=await ResolvePaymentLocationAccountCoreAsync(_context,paymentLocationId);
            var newAccount=await _context.Accounts.AsNoTracking().SingleAsync(x=>x.Id==newAccountId);
            var oldAccountId=sent.PaidFromAccountId??root.PaidFromAccountId
                ??throw new InvalidOperationException("حساب مقصد قبلی حواله یافت نشد.");
            var owners=new[]{root.CorrespondentId,sent.CorrespondentId,newAccount.CorrespondentId}
                .Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().OrderBy(x=>x).ToArray();
            await LockOutgoingNumbersAsync(owners);
            if(root.PaymentLocationId==paymentLocationId && sent.PaymentLocationId==paymentLocationId &&
               sent.CorrespondentId==newAccount.CorrespondentId && oldAccountId==newAccountId)
            {
                if(transaction is not null)await transaction.CommitAsync();
                return (await GetHawalaByIdAsync(id))!;
            }
            if(await _context.AgentPaymentBillItems.AnyAsync(x=>x.HawalaId==sent.Id && x.IsActive))
                throw new InvalidOperationException("حواله ارسالی در بیل پرداخت قرار دارد؛ ابتدا بیل پرداخت‌نشده را لغو کنید. بیل پرداخت‌شده قابل تغییر نیست.");
            if(oldAccountId!=newAccountId && await _context.MoneyExchangeOperations.AnyAsync(x=>x.LinkedHawalaId==sent.Id && !x.IsDeleted))
                throw new InvalidOperationException("خود حواله ارسالی تبدیل مستقل اسعار دارد؛ ابتدا تبدیل مستقل مقصد را برگشت دهید. تبدیل مبدأ کویته نیازی به برگشت ندارد.");
            if(oldAccountId!=newAccountId && sent.Status=="Paid" && sent.PaidAt.HasValue &&
               await _context.Correspondents.AnyAsync(x=>x.Id==sent.CorrespondentId && x.RequiresManualExecution))
                throw new InvalidOperationException("اجرای حواله در مقصد قبلی تأیید شده است؛ اصلاح محل پرداخت نیازمند برگشت اجرای قبلی است.");
            var now=DateTime.UtcNow;
            if(await _context.CorrespondentAccountPeriods.AnyAsync(x=>owners.Contains(x.CorrespondentId) && x.PeriodTo>=now))
                throw new InvalidOperationException("برای ثبت اصلاحیه مقصد، دوره باز در تاریخ امروز لازم است.");
            var oldValue=JsonSerializer.Serialize(new{root.PaymentLocationId,root.PaidFromAccountId,SentId=sent.Id,sent.Number,sent.CorrespondentId});
            var correction=new ImportCommissionCorrectionService(_context);
            var commissions=await correction.ReverseAsync([sent.Id]);
            // Legacy forwarding commissions use the parent id. Incoming commissions are deliberately excluded.
            var legacy=await correction.ReverseAsync([root.Id],scope:"Origin");
            var outgoingSettlement=oldAccountId!=newAccountId?await DetachOutgoingSettlementForLocationAsync(sent):null;
            if(oldAccountId!=newAccountId)
            {
                var oldEntries=await _context.LedgerEntries.AsNoTracking().Where(x=>x.HawalaId==sent.Id)
                    .GroupBy(x=>new{x.AccountId,x.CurrencyId})
                    .Select(g=>new{g.Key.AccountId,g.Key.CurrencyId,Net=g.Sum(x=>x.TalabKar-x.BadehKar)}).ToListAsync();
                if(oldEntries.Count==0)throw new InvalidOperationException("سند حسابداری حواله ارسالی یافت نشد.");
                var expected=new Dictionary<long,decimal>{{root.ToCurrencyId,root.ToAmount??root.FromAmount}};
                if(sent.AgentCommissionAmount is > 0)
                {
                    var currency=sent.AgentCommissionCurrencyId??root.ToCurrencyId;
                    expected[currency]=expected.GetValueOrDefault(currency)+sent.AgentCommissionAmount.Value;
                }
                var actual=oldEntries.Where(x=>x.AccountId==oldAccountId && x.Net!=0).ToDictionary(x=>x.CurrencyId,x=>x.Net);
                if(actual.Count!=expected.Count || expected.Any(x=>actual.GetValueOrDefault(x.Key)!=x.Value))
                    throw new InvalidOperationException("مانده حسابداری مقصد قبلی با مبلغ و کمیشن حواله مطابقت ندارد؛ اصلاح خودکار متوقف شد.");
                foreach(var entry in oldEntries.Where(x=>x.Net!=0))
                    await CreateLedgerEntry(sent.Id,entry.AccountId,entry.CurrencyId,Math.Max(-entry.Net,0),Math.Max(entry.Net,0),
                        "اصلاح محل پرداخت — برگشت حسابداری مقصد قبلی",now);
                await ProcessHawalaReceivePaymentLedgerAsync(root,newAccount,sent.Id,sent.AgentCommissionAmount,sent.AgentCommissionCurrencyId,now);
            }
            if(sent.CorrespondentId!=newAccount.CorrespondentId)
                sent.Number=await GetNextNumberAsync(newAccount.CorrespondentId!.Value,"HawalaSend");
            sent.CorrespondentId=newAccount.CorrespondentId;
            sent.PaidFromAccountId=newAccountId;
            sent.PaymentLocationId=paymentLocationId;
            root.PaymentLocationId=paymentLocationId;
            root.PaidFromAccountId=newAccountId;
            if(oldAccountId!=newAccountId)
            {
                var manual=await _context.Correspondents.Where(x=>x.Id==newAccount.CorrespondentId).Select(x=>x.RequiresManualExecution).SingleAsync();
                sent.Status=manual?"Pending":"Paid";
                sent.PaidAt=manual?null:now;
                sent.PaidBy=manual?null:_context.RequireCurrentUserId();
            }
            var locationName=await _context.PaymentLocations.Where(x=>x.Id==paymentLocationId).Select(x=>x.Name).SingleAsync();
            var rows=await _context.HawalaImportRows.Where(x=>x.HawalaId==root.Id || x.GeneratedSendHawalaId==sent.Id).ToListAsync();
            foreach(var row in rows)
            {
                row.PaymentLocationId=paymentLocationId;row.PaymentLocationText=locationName;
                row.DestinationCorrespondentId=newAccount.CorrespondentId;
            }
            await _context.SaveChangesAsync();
            if(outgoingSettlement is not null && await _context.Correspondents.AnyAsync(x=>x.Id==sent.CorrespondentId && x.SettlementCurrencyId!=null))
                await ReconvertSettlementAsync(sent,outgoingSettlement,useDailyRates:true);
            await correction.RepostAsync(commissions,[sent.Id],false,resolveLocationRates:true);
            await correction.RepostAsync(legacy,[root.Id],false,resolveLocationRates:true);
            _context.AuditLogs.Add(new AuditLog{UserId=_context.RequireCurrentUserId(),Action="CHANGE_PAYMENT_LOCATION",TableName="Hawalas",RecordId=root.Id,
                OldValue=oldValue,NewValue=JsonSerializer.Serialize(new{PaymentLocationId=paymentLocationId,SentId=sent.Id,sent.Number,sent.CorrespondentId,NewAccountId=newAccountId,SourceSettlementPreserved=true})});
            await _context.SaveChangesAsync();
            if(transaction is not null)await transaction.CommitAsync();
            return (await GetHawalaByIdAsync(id))!;
        }
        catch
        {
            if(transaction is not null)await transaction.RollbackAsync();
            else if(savepoint is not null)await _context.Database.CurrentTransaction!.RollbackToSavepointAsync(savepoint);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<SettlementReconversionState?> DetachOutgoingSettlementForLocationAsync(Hawala sent)
    {
        var link=await _context.CorrespondentSettlementConversionHawalas.Include(x=>x.Conversion).ThenInclude(x=>x.Transaction)
            .SingleOrDefaultAsync(x=>x.HawalaId==sent.Id);
        if(link is null)return null;
        if(link.Conversion.SourceMode!="Hawalas" || link.Conversion.CorrespondentId!=sent.CorrespondentId)
            throw new InvalidOperationException("تبدیل کلی حساب مقصد، سهم مستقل این حواله را ندارد؛ ابتدا همان تبدیل مقصد را برگشت دهید.");
        var items=await _context.CorrespondentSettlementConversionHawalaItems.Include(x=>x.LedgerEntries)
            .Where(x=>x.ConversionId==link.ConversionId && x.HawalaId==sent.Id).ToListAsync();
        if(items.Count==0 || items.Any(x=>x.LedgerEntries.Count==0))
            throw new InvalidOperationException("جزئیات حسابداری سهم تبدیل حواله ارسالی کامل نیست؛ اصلاح خودکار مجاز نیست.");
        if(items.SelectMany(x=>x.LedgerEntries).GroupBy(x=>x.CurrencyId).Any(g=>g.Sum(x=>x.TalabKar-x.BadehKar)!=0))
            throw new InvalidOperationException("سند سهم تبدیل حواله ارسالی متوازن نیست؛ اصلاح خودکار متوقف شد.");
        var previous=new SettlementReconversionState(link.Conversion.CorrespondentId,link.Conversion.TargetCurrencyId,
            items.ToDictionary(x=>x.SourceCurrencyId,x=>x.ExchangeRate));
        var reversal=new Transaction{TransactionNo="SLC-"+Guid.NewGuid().ToString("N"),TransactionType="CorrespondentSettlementReversal",Status="Paid",
            CreatedBy=_context.RequireCurrentUserId(),BranchId=await _context.GetDefaultBranchIdAsync(),ReversedTransactionId=link.Conversion.TransactionId,
            Remarks=$"اصلاح محل پرداخت حواله ارسالی {sent.Id}؛ برگشت فقط سهم همین حواله از تبدیل {link.ConversionId}"};
        _context.Transactions.Add(reversal);await _context.SaveChangesAsync();
        foreach(var entry in items.SelectMany(x=>x.LedgerEntries))
        {
            // Retain original journals (including closed periods); append the inverse today.
            _context.LedgerEntries.Add(new(){TransactionId=reversal.Id,AccountId=entry.AccountId,CurrencyId=entry.CurrencyId,
                TalabKar=entry.BadehKar,BadehKar=entry.TalabKar,Description=$"اصلاح محل پرداخت — برگشت سهم تبدیل حواله ارسالی {sent.Id}"});
            entry.SettlementHawalaItemId=null;
        }
        await _context.SaveChangesAsync();
        _context.CorrespondentSettlementConversionHawalaItems.RemoveRange(items);
        _context.CorrespondentSettlementConversionHawalas.Remove(link);
        await _context.SaveChangesAsync();
        var settlement=new CorrespondentSettlementService(_context);
        await settlement.MarkDetachedAsync(link.ConversionId,sent.Id);
        if(await _context.CorrespondentSettlementConversionHawalaItems.AnyAsync(x=>x.ConversionId==link.ConversionId))
            await settlement.CaptureAsync(link.ConversionId,null,true);
        else
        {
            link.Conversion.Transaction.Status="Cancel";
            link.Conversion.Transaction.CancelledBy=_context.RequireCurrentUserId();
            link.Conversion.Transaction.CancelledAt=DateTime.UtcNow;
            link.Conversion.Transaction.CancelReason="برگشت سهم تبدیل پس از اصلاح محل پرداخت";
            if(_context.Database.IsNpgsql())await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"CorrespondentSettlementHistorySnapshots\" SET \"Status\"='Reversed' WHERE \"TenantId\"={_context.CurrentTenantId} AND \"ConversionId\"={link.ConversionId} AND \"Status\" IN('Active','Adjusted')");
            else await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.CorrespondentSettlementHistorySnapshots SET Status=N'Reversed' WHERE TenantId={_context.CurrentTenantId} AND ConversionId={link.ConversionId} AND Status IN(N'Active',N'Adjusted')");
            await _context.SaveChangesAsync();
        }
        return previous;
    }
}
