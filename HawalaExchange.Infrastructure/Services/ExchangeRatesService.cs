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

        // ✅ Override CreateAsync to set CreatedBy
        public override async Task<ExchangeRateDto> CreateAsync(CreateExchangeRateDto createDto)
        {
            // Map DTO to entity
            var entity = _mapper.Map<ExchangeRate>(createDto);

            // ✅ Set the CreatedBy field (temporary hardcoded to user ID 1)
            // Replace this with the actual logged-in user ID from your auth system
            entity.CreatedBy = 1;

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