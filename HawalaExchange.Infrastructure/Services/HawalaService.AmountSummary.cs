using HawalaExchange.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    // Unmapped read model: the stored principal valuation excludes commissions.
    public sealed class SummaryValuation
    {
        public long HawalaId { get; set; }
        public long ConversionId { get; set; }
        public long SourceCurrencyId { get; set; }
        public long TargetCurrencyId { get; set; }
        public decimal TargetAmount { get; set; }
    }
    public async Task<HawalaAmountSummaryDto> GetAmountSummaryAsync(HawalaAmountSummaryRequest request,CancellationToken cancellationToken=default)
    {
        var ids=(request.SelectedIds??[]).Distinct().ToArray();
        if(ids.Length>50000 || ids.Any(x=>x<=0))throw new InvalidOperationException("تعداد یا شناسه حواله‌های انتخاب‌شده معتبر نیست.");
        var query=ids.Length==0?BuildHawalaListQuery(request.Filter):_context.Hawalas.AsNoTracking().Where(x=>!x.IsDeleted && ids.Contains(x.Id));
        if(request.ScopeCorrespondentId.HasValue)query=query.Where(x=>x.CorrespondentId==request.ScopeCorrespondentId);
        if(!string.IsNullOrWhiteSpace(request.ScopeHawalaType))query=query.Where(x=>x.HawalaType==request.ScopeHawalaType);
        var originals=await query.GroupBy(h=>new{Currency=h.HawalaType=="HawalaSend"?h.ToCurrency.Code:h.FromCurrency.Code,Cancelled=h.Status=="Cancel"})
            .Select(g=>new HawalaOriginalAmountTotal{Currency=g.Key.Currency,Cancelled=g.Key.Cancelled,Count=g.Count(),Amount=g.Sum(h=>h.HawalaType=="HawalaSend"?h.ToAmount??h.FromAmount:h.FromAmount)})
            .OrderBy(x=>x.Currency).ToListAsync(cancellationToken);
        var count=originals.Sum(x=>x.Count);
        if(ids.Length>0 && count!=ids.Length)throw new InvalidOperationException("بعضی انتخاب‌ها حذف شده یا مربوط به این فهرست نیستند؛ انتخاب‌ها را تازه کنید.");
        var rows=BuildAmountRows(query);
        var amounts=from r in rows
            join currency in _context.Currencies on r.TargetCurrency equals (long?)currency.Id
            group r by new{r.CorrespondentId,r.Correspondent,r.Cancelled,Currency=currency.Code} into g
            select new HawalaSettlementAmountTotal{
                Count=g.Count(),CorrespondentId=g.Key.CorrespondentId,Correspondent=g.Key.Correspondent,Currency=g.Key.Currency,Cancelled=g.Key.Cancelled,
                NativeAmount=g.Sum(x=>x.PrincipalCurrency==x.TargetCurrency?x.Principal:0),
                ConvertedAmount=g.Sum(x=>x.PrincipalCurrency!=x.TargetCurrency?x.Converted??0:0),
                UnconvertedCount=g.Count(x=>x.PrincipalCurrency!=x.TargetCurrency && x.Converted==null && !x.HasConversion),
                IncompleteCount=g.Count(x=>x.PrincipalCurrency!=x.TargetCurrency && x.Converted==null && x.HasConversion)};
        var settlements=await amounts.OrderBy(x=>x.Correspondent).ThenBy(x=>x.Currency).ToListAsync(cancellationToken);
        return new(){Count=count,SelectedOnly=ids.Length>0,Originals=originals,Settlements=settlements,WithoutAgreementCount=count-settlements.Sum(x=>x.Count)};
    }
    private sealed class SummaryAmountRow
    {
        public long HawalaId { get; set; }
        public long? CorrespondentId { get; set; }
        public string Correspondent { get; set; } = "";
        public bool Cancelled { get; set; }
        public long PrincipalCurrency { get; set; }
        public string Currency { get; set; } = "";
        public decimal Principal { get; set; }
        public long? TargetCurrency { get; set; }
        public string? TargetCurrencyCode { get; set; }
        public decimal? Converted { get; set; }
        public bool HasConversion { get; set; }
    }
    private IQueryable<SummaryAmountRow> BuildAmountRows(IQueryable<HawalaExchange.Domain.Entities.Hawala> query)
    {
        var valuations=_context.Database.IsNpgsql()
            ?_context.Database.SqlQuery<SummaryValuation>($"SELECT \"HawalaId\",\"ConversionId\",\"SourceCurrencyId\",\"TargetCurrencyId\",\"TargetAmount\" FROM public.\"CorrespondentSettlementValuations\" WHERE \"TenantId\"={_context.CurrentTenantId}")
            :_context.Database.SqlQuery<SummaryValuation>($"SELECT HawalaId,ConversionId,SourceCurrencyId,TargetCurrencyId,TargetAmount FROM dbo.CorrespondentSettlementValuations WHERE TenantId={_context.CurrentTenantId}");
        var activeValuations=from v in valuations
            join c in _context.CorrespondentSettlementConversions on v.ConversionId equals c.Id
            where c.Transaction.Status!="Cancel"
            select new{HawalaId=(long?)v.HawalaId,TargetCurrencyId=(long?)v.TargetCurrencyId,TargetAmount=(decimal?)v.TargetAmount};
        var activeLinks=_context.CorrespondentSettlementConversionHawalas.Where(x=>x.Conversion.Transaction.Status!="Cancel")
            .Select(x=>(long?)x.HawalaId).Distinct();
        return from h in query
            join v in activeValuations on (long?)h.Id equals v.HawalaId into joined
            from v in joined.DefaultIfEmpty()
            join link in activeLinks on (long?)h.Id equals link into links
            from link in links.DefaultIfEmpty()
            join currency in _context.Currencies on (v.TargetCurrencyId??h.Correspondent!.SettlementCurrencyId) equals (long?)currency.Id into currencies
            from currency in currencies.DefaultIfEmpty()
            select new SummaryAmountRow{
                HawalaId=h.Id,Currency=h.HawalaType=="HawalaSend"?h.ToCurrency.Code:h.FromCurrency.Code,
                TargetCurrencyCode=currency.Code,
                CorrespondentId=h.CorrespondentId,Correspondent=h.Correspondent==null?"بدون نمایندگی":h.Correspondent.Name,Cancelled=h.Status=="Cancel",
                PrincipalCurrency=h.HawalaType=="HawalaSend"?h.ToCurrencyId:h.FromCurrencyId,
                Principal=h.HawalaType=="HawalaSend"?h.ToAmount??h.FromAmount:h.FromAmount,
                TargetCurrency=v.TargetCurrencyId??h.Correspondent!.SettlementCurrencyId,
                Converted=v.TargetAmount,HasConversion=link.HasValue};
    }
    public async Task<List<HawalaBrowserAmountRow>> GetSelectionAmountsAsync(long[] ids,CancellationToken cancellationToken=default)
    {
        ids=(ids??[]).Distinct().ToArray();
        if(ids.Length>50000 || ids.Any(x=>x<=0))throw new InvalidOperationException("انتخاب حواله‌ها معتبر نیست.");
        if(ids.Length==0)return [];
        var rows=await BuildAmountRows(_context.Hawalas.AsNoTracking().Where(x=>!x.IsDeleted && ids.Contains(x.Id))).ToListAsync(cancellationToken);
        return rows.Select(x=>new HawalaBrowserAmountRow{Id=x.HawalaId,CorrespondentId=x.CorrespondentId,Correspondent=x.Correspondent,
            Currency=x.Currency,Original=x.Principal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SettlementCurrency=x.TargetCurrencyCode,Converted=x.Converted?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Cancelled=x.Cancelled,HasConversion=x.HasConversion}).ToList();
    }

}
