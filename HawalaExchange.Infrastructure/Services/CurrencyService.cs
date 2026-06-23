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
    public class CurrencyService : ICurrencyService
    {
        public readonly ApplicationDbContext _context;
        public CurrencyService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<CurrencyDtos>> GetAllAsync()
        {
            return await Task.FromResult(_context.Currencies.Select(c => new CurrencyDtos
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                Symbol = c.Symbol,
                DecimalPlaces = c.DecimalPlaces,
                IsActive = c.IsActive
            }).ToList());
        }

        public async Task<CurrencyDtos?> GetByIdAsync(long id)
        {
            var currency = await _context.Currencies
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CurrencyDtos
                {
                    Id = c.Id,
                    Code = c.Code,
                    Name = c.Name,
                    Symbol = c.Symbol,
                    DecimalPlaces = c.DecimalPlaces,
                    IsActive = c.IsActive
                }).FirstOrDefaultAsync();
            if (currency == null) return null;
        }
        public async Task<long> CreateAsync(CreateCurrencyRequest request)
        {
            var exist = await _context.Currencies.AnyAsync(c => c.Code == request.Code);
            if (exist) throw new InvalidOperationException("Currency with the same code already exists.");

            var currency = new Currency
            {
                Code = request.Code,
                Name = request.Name,
                Symbol = request.Symbol,
                DecimalPlaces = request.DecimalPlaces,
                IsActive = request.IsActive
            };

            _context.Currencies.Add(currency);
            await _context.SaveChangesAsync();
            return currency.Id;
        }

        public async Task DeleteAsync(long id)
        {
            var currency = await _context.Currencies.FindAsync(id);
            if (currency == null)
                { 
                    throw new InvalidOperationException("Currency not found.");
                }
            _context.Currencies.Remove(currency);
            await _context.SaveChangesAsync();
        }

        

        

        public Task UpdateAsync(long id, UpdateCurrencyRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
