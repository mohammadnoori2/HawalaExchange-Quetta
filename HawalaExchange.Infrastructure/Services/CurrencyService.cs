using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class CurrencyService : BaseService<Currency, CurrencyDto, CreateCurrencyDto, UpdateCurrencyDto>, ICurrencyService
    {
        public CurrencyService(ApplicationDbContext context, IMapper mapper)
            : base(context, mapper) { }

        public async Task<CurrencyDto?> GetByCodeAsync(string code)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(c => c.Code == code);
            return entity == null ? null : _mapper.Map<CurrencyDto>(entity);
        }

        public async Task<IEnumerable<CurrencyDto>> GetActiveCurrenciesAsync()
        {
            var entities = await _dbSet.Where(c => c.IsActive).ToListAsync();
            return _mapper.Map<IEnumerable<CurrencyDto>>(entities);
        }

        public async Task<CurrencyDto> DeactivateAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Currency with ID {id} not found.");

            entity.IsActive = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<CurrencyDto>(entity);
        }

        public async Task<CurrencyDto> ActivateAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Currency with ID {id} not found.");

            entity.IsActive = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<CurrencyDto>(entity);
        }

        protected override async Task ValidateCreateAsync(Currency entity, CreateCurrencyDto dto)
        {
            if (await _dbSet.AnyAsync(c => c.Code == entity.Code))
                throw new InvalidOperationException($"Currency with code '{entity.Code}' already exists.");
        }
    }
}