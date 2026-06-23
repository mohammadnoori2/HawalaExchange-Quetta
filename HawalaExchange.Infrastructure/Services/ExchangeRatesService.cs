using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using YourNamespace.Data;
using YourNamespace.Entities;

namespace HawalaExchange.Infrastructure.Services
{
    public class ExchangeRatesService : IExchangeRatesService
    {
        private readonly ApplicationDbContext _context;
        public ExchangeRatesService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<ExchangeRatesDtos>> GetAllAsync()
        {
            return await _context.ExchangeRates
                .AsNoTracking()
                .OrderBy(x => x.EffectiveDate)
                .Select(x => new ExchangeRatesDtos
                {
                    Id = x.Id,
                    FromCurrencyId = x.FromCurrencyId,
                    ToCurrencyId = x.ToCurrencyId,
                    BuyRate = x.BuyRate,
                    SellRate = x.SellRate,
                    EffectiveDate = x.EffectiveDate,
                    CreatedBy = x.CreatedBy
                })
                .ToListAsync();
        }

        public async Task<ExchangeRatesDtos?> GetByIdAsync(long id)
        {
            var entity = await _context.ExchangeRates
                .AsNoTracking()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();

            if (entity == null)
            {
                return null;
            }

            return new ExchangeRatesDtos
            {
                Id = entity.Id,
                FromCurrencyId = entity.FromCurrencyId,
                ToCurrencyId = entity.ToCurrencyId,
                BuyRate = entity.BuyRate,
                SellRate = entity.SellRate,
                EffectiveDate = entity.EffectiveDate,
                CreatedBy = entity.CreatedBy
            };
        }

        public async Task<long> CreateAsync(CreateExchangeRatesRequest request)
        {
            var entity = new ExchangeRate
            {
                FromCurrencyId = request.FromCurrencyId,
                ToCurrencyId = request.ToCurrencyId,
                BuyRate = request.BuyRate,
                SellRate = request.SellRate,
                EffectiveDate = request.EffectiveDate,
                CreatedBy = request.CreatedBy
            };

            _context.ExchangeRates.Add(entity);
            await _context.SaveChangesAsync();
            return entity.Id;
        }

        
        public async Task UpdateAsync(long id, UpdateExchangeRatesRequest request)
        {
            var existing = await _context.ExchangeRates.FindAsync(id);
            if (existing == null)
            {
                throw new Exception("Exchange rate not found.");
            }   

            existing.FromCurrencyId = request.FromCurrencyId;
            existing.ToCurrencyId = request.ToCurrencyId;
            existing.BuyRate = request.BuyRate;
            existing.SellRate = request.SellRate;
            existing.EffectiveDate = request.EffectiveDate;
            existing.CreatedBy = request.CreatedBy;
            _context.ExchangeRates.Update(existing);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(long id)
        {
            var entity = await _context.ExchangeRates.FindAsync(id);
            if (entity == null)
            {
                throw new Exception("Exchange rate not found.");
            }
            _context.ExchangeRates.Remove(entity);
            _context.SaveChanges();
        }
    }
}
