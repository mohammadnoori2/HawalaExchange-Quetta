using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class AccountBadehkarLimitService : BaseService<AccountBadehkarLimit, AccountBadehkarLimitDto, CreateAccountBadehkarLimitDto, UpdateAccountBadehkarLimitDto>, IAccountBadehkarLimitService
    {
        public AccountBadehkarLimitService(ApplicationDbContext context, IMapper mapper)
            : base(context, mapper) { }

        // ===== متدهای سفارشی =====

        public async Task<AccountBadehkarLimitDto?> GetByAccountAndCurrencyAsync(long accountId, long currencyId)
        {
            var entity = await _dbSet
                .FirstOrDefaultAsync(l => l.AccountId == accountId && l.CurrencyId == currencyId);
            return entity == null ? null : _mapper.Map<AccountBadehkarLimitDto>(entity);
        }

        public async Task<IEnumerable<AccountBadehkarLimitDto>> GetByAccountAsync(long accountId)
        {
            var entities = await _dbSet
                .Where(l => l.AccountId == accountId)
                .Include(l => l.Account)
                .Include(l => l.Currency)
                .Include(l => l.CreatedByUser)
                .OrderByDescending(l => l.Id)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountBadehkarLimitDto>>(entities);
        }

        public async Task<IEnumerable<AccountBadehkarLimitDto>> GetByCurrencyAsync(long currencyId)
        {
            var entities = await _dbSet
                .Where(l => l.CurrencyId == currencyId)
                .Include(l => l.Account)
                .Include(l => l.Currency)
                .Include(l => l.CreatedByUser)
                .OrderByDescending(l => l.Id)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountBadehkarLimitDto>>(entities);
        }

        public async Task<IEnumerable<AccountBadehkarLimitDto>> GetActiveLimitsAsync()
        {
            var entities = await _dbSet
                .Where(l => l.IsActive)
                .Include(l => l.Account)
                .Include(l => l.Currency)
                .Include(l => l.CreatedByUser)
                .OrderByDescending(l => l.Id)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountBadehkarLimitDto>>(entities);
        }

        public async Task<AccountBadehkarLimitDto> ActivateAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"محدودیت با شناسه {id} یافت نشد.");

            entity.IsActive = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<AccountBadehkarLimitDto>(entity);
        }

        public async Task<AccountBadehkarLimitDto> DeactivateAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"محدودیت با شناسه {id} یافت نشد.");

            entity.IsActive = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<AccountBadehkarLimitDto>(entity);
        }

        // ===== بازنویسی متد CreateAsync برای تنظیم CreatedBy و اعتبارسنجی =====

        public override async Task<AccountBadehkarLimitDto> CreateAsync(CreateAccountBadehkarLimitDto createDto)
        {
            // بررسی تکراری نبودن (AccountId + CurrencyId)
            var exists = await _dbSet.AnyAsync(l =>
                l.AccountId == createDto.AccountId &&
                l.CurrencyId == createDto.CurrencyId);
            if (exists)
                throw new InvalidOperationException($"محدودیت برای حساب {createDto.AccountId} و ارز {createDto.CurrencyId} قبلاً تعریف شده است.");

            var entity = _mapper.Map<AccountBadehkarLimit>(createDto);

            // ✅ تنظیم CreatedBy (موقتاً 1 – در آینده از کاربر جاری دریافت می‌شود)
            entity.CreatedBy = 1;
            entity.CreatedAt = DateTime.UtcNow;
            entity.IsActive = true;

            await _dbSet.AddAsync(entity);
            await _context.SaveChangesAsync();

            return _mapper.Map<AccountBadehkarLimitDto>(entity);
        }

        // ===== بازنویسی متد UpdateAsync برای جلوگیری از تغییر AccountId و CurrencyId =====

        public override async Task<AccountBadehkarLimitDto> UpdateAsync(long id, UpdateAccountBadehkarLimitDto updateDto)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"محدودیت با شناسه {id} یافت نشد.");

            // فقط BadehkarLimit و IsActive قابل‌تغییر هستند
            entity.BadehkarLimit = updateDto.BadehkarLimit;
            entity.IsActive = updateDto.IsActive;

            _dbSet.Update(entity);
            await _context.SaveChangesAsync();

            return _mapper.Map<AccountBadehkarLimitDto>(entity);
        }

        // ===== متدهای اعتبارسنجی (اختیاری) =====

        protected override async Task ValidateDeleteAsync(AccountBadehkarLimit entity)
        {
            // در صورت نیاز، می‌توانید بررسی کنید که آیا این محدودیت در حال استفاده است یا خیر
            // مثلاً بررسی کنید که آیا تراکنشی با این محدودیت در حال انجام است
        }
    }
}