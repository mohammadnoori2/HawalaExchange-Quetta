using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class ExchangeRateService : BaseService<ExchangeRate, ExchangeRateDto, CreateExchangeRateDto, UpdateExchangeRateDto>, IExchangeRateService
    {
        public ExchangeRateService(ApplicationDbContext context, IMapper mapper)
            : base(context, mapper) { }

        public async Task<DashboardExchangeRatesDto> GetDashboardRatesAsync()
        {
            var zoneId=await (_context.Database.IsNpgsql()
                ?_context.Database.SqlQuery<string>($"SELECT \"TimeZoneId\" AS \"Value\" FROM public.\"TenantOperationalControls\" WHERE \"TenantId\"={_context.CurrentTenantId}")
                :_context.Database.SqlQuery<string>($"SELECT TimeZoneId AS [Value] FROM dbo.TenantOperationalControls WHERE TenantId={_context.CurrentTenantId}")).SingleOrDefaultAsync();
            var now=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zoneId)?"Asia/Kabul":zoneId));
            var end=now.Date.AddDays(1);
            var latest=await _dbSet.AsNoTracking().Where(x=>x.EffectiveDate<end && x.FromCurrency!.IsActive && x.ToCurrency!.IsActive)
                .GroupBy(x=>new{x.FromCurrencyId,x.ToCurrencyId})
                .Select(g=>g.OrderByDescending(x=>x.EffectiveDate).ThenByDescending(x=>x.Id).First()).ToListAsync();
            var ids=latest.SelectMany(x=>new[]{x.FromCurrencyId,x.ToCurrencyId}).Distinct().ToArray();
            var codes=await _context.Currencies.AsNoTracking().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Code);
            return new(){LocalNow=now,Rates=latest.Select(x=>new ExchangeRateDto{Id=x.Id,FromCurrencyId=x.FromCurrencyId,ToCurrencyId=x.ToCurrencyId,
                FromCurrencyCode=codes[x.FromCurrencyId],ToCurrencyCode=codes[x.ToCurrencyId],BuyRate=x.BuyRate,SellRate=x.SellRate,EffectiveDate=x.EffectiveDate})
                .OrderBy(x=>x.FromCurrencyCode).ThenBy(x=>x.ToCurrencyCode).ToList()};
        }

        // ✅ Override CreateAsync to set CreatedBy
        public override async Task<ExchangeRateDto> CreateAsync(CreateExchangeRateDto createDto)
        {
            // Map DTO to entity
            var entity = _mapper.Map<ExchangeRate>(createDto);

            entity.CreatedBy = _context.RequireCurrentUserId();
            _context.PrepareTenantEntity(entity);

            // Validate (checks that from/to currencies are different)
            await ValidateCreateAsync(entity, createDto);

            // Add and save
            await _dbSet.AddAsync(entity);
            await _context.SaveChangesAsync();

            return _mapper.Map<ExchangeRateDto>(entity);
        }

        // --- All other methods remain unchanged ---

        public async Task<ExchangeRateDto?> GetLatestRateAsync(long fromCurrencyId, long toCurrencyId)
        {
            var rate = await _dbSet
                .Where(r => r.FromCurrencyId == fromCurrencyId && r.ToCurrencyId == toCurrencyId)
                .OrderByDescending(r => r.EffectiveDate)
                .FirstOrDefaultAsync();
            return rate == null ? null : _mapper.Map<ExchangeRateDto>(rate);
        }

        public async Task<decimal> ConvertAsync(long fromCurrencyId, long toCurrencyId, decimal amount)
        {
            if (fromCurrencyId == toCurrencyId) return amount;

            var rate = await GetLatestRateAsync(fromCurrencyId, toCurrencyId);
            if (rate == null)
                throw new InvalidOperationException($"Exchange rate not found from {fromCurrencyId} to {toCurrencyId}.");

            return amount * rate.SellRate;
        }

        public async Task<ExchangeRateDto?> GetRateByDateAsync(long fromCurrencyId, long toCurrencyId, DateTime date)
        {
            var rate = await _dbSet
                .Where(r =>
                    r.FromCurrencyId == fromCurrencyId &&
                    r.ToCurrencyId == toCurrencyId &&
                    r.EffectiveDate.Date <= date.Date)
                .OrderByDescending(r => r.EffectiveDate)
                .FirstOrDefaultAsync();
            return rate == null ? null : _mapper.Map<ExchangeRateDto>(rate);
        }

        public async Task<IEnumerable<ExchangeRateDto>> GetRateHistoryAsync(long fromCurrencyId, long toCurrencyId, DateTime fromDate, DateTime toDate)
        {
            var rates = await _dbSet
                .Where(r =>
                    r.FromCurrencyId == fromCurrencyId &&
                    r.ToCurrencyId == toCurrencyId &&
                    r.EffectiveDate >= fromDate &&
                    r.EffectiveDate <= toDate)
                .OrderBy(r => r.EffectiveDate)
                .ToListAsync();
            return _mapper.Map<IEnumerable<ExchangeRateDto>>(rates);
        }

        public async Task<ExchangeRateDto?> GetCurrentBuyRateAsync(long fromCurrencyId, long toCurrencyId)
        {
            return await GetLatestRateAsync(fromCurrencyId, toCurrencyId);
        }

        public async Task<ExchangeRateDto?> GetCurrentSellRateAsync(long fromCurrencyId, long toCurrencyId)
        {
            return await GetLatestRateAsync(fromCurrencyId, toCurrencyId);
        }

        protected override async Task ValidateCreateAsync(ExchangeRate entity, CreateExchangeRateDto dto)
        {
            if (entity.FromCurrencyId == entity.ToCurrencyId)
                throw new InvalidOperationException("From currency and To currency cannot be the same.");
        }
    }
}
