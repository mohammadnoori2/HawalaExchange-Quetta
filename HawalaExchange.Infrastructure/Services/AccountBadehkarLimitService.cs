using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class AccountBadehkarLimitService
    : BaseService<AccountBadehkarLimit, AccountBadehkarLimitDto, CreateAccountBadehkarLimitDto, UpdateAccountBadehkarLimitDto>,
      IAccountBadehkarLimitService
{
    public AccountBadehkarLimitService(ApplicationDbContext context, IMapper mapper)
        : base(context, mapper)
    {
    }

    public override async Task<IEnumerable<AccountBadehkarLimitDto>> GetAllAsync()
    {
        var entities = await LimitsQuery()
            .Where(x =>
                x.Account!.AccountType == "Customer" ||
                x.Account.AccountType == "Correspondent" ||
                x.Account.CustomerId != null ||
                x.Account.CorrespondentId != null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        return await ToDtosAsync(entities);
    }

    public override async Task<AccountBadehkarLimitDto?> GetByIdAsync(long id)
    {
        var entity = await LimitsQuery().FirstOrDefaultAsync(x => x.Id == id);
        return entity == null ? null : (await ToDtosAsync([entity])).Single();
    }

    public async Task<AccountBadehkarLimitDto?> GetByAccountAndCurrencyAsync(
        long accountId,
        long currencyId)
    {
        var entity = await LimitsQuery().FirstOrDefaultAsync(x =>
            x.AccountId == accountId && x.CurrencyId == currencyId);
        return entity == null ? null : (await ToDtosAsync([entity])).Single();
    }

    public async Task<IEnumerable<AccountBadehkarLimitDto>> GetByAccountAsync(long accountId)
    {
        var entities = await LimitsQuery()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.Id)
            .ToListAsync();
        return await ToDtosAsync(entities);
    }

    public async Task<IEnumerable<AccountBadehkarLimitDto>> GetByCurrencyAsync(long currencyId)
    {
        var entities = await LimitsQuery()
            .Where(x =>
                x.CurrencyId == currencyId &&
                (x.Account!.AccountType == "Customer" ||
                 x.Account.AccountType == "Correspondent" ||
                 x.Account.CustomerId != null ||
                 x.Account.CorrespondentId != null))
            .OrderByDescending(x => x.Id)
            .ToListAsync();
        return await ToDtosAsync(entities);
    }

    public async Task<IEnumerable<AccountBadehkarLimitDto>> GetActiveLimitsAsync()
    {
        var entities = await LimitsQuery()
            .Where(x =>
                x.IsActive &&
                (x.Account!.AccountType == "Customer" ||
                 x.Account.AccountType == "Correspondent" ||
                 x.Account.CustomerId != null ||
                 x.Account.CorrespondentId != null))
            .OrderByDescending(x => x.Id)
            .ToListAsync();
        return await ToDtosAsync(entities);
    }

    public async Task<AccountBadehkarLimitDto> ActivateAsync(long id)
    {
        var entity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null)
            throw new KeyNotFoundException($"محدودیت با شناسه {id} یافت نشد.");

        await ValidateTargetAsync(entity.AccountId, entity.CurrencyId, entity.BadehkarLimit);
        entity.IsActive = true;
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(id))!;
    }

    public async Task<AccountBadehkarLimitDto> DeactivateAsync(long id)
    {
        var entity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null)
            throw new KeyNotFoundException($"محدودیت با شناسه {id} یافت نشد.");

        entity.IsActive = false;
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(id))!;
    }

    public override async Task<AccountBadehkarLimitDto> CreateAsync(
        CreateAccountBadehkarLimitDto createDto)
    {
        await ValidateTargetAsync(
            createDto.AccountId,
            createDto.CurrencyId,
            createDto.BadehkarLimit);

        var exists = await _dbSet.AnyAsync(x =>
            x.AccountId == createDto.AccountId &&
            x.CurrencyId == createDto.CurrencyId);
        if (exists)
        {
            throw new InvalidOperationException(
                "برای این حساب و ارز قبلاً سقف بدهکاری تعریف شده است؛ همان مورد را ویرایش یا فعال کنید.");
        }

        var entity = new AccountBadehkarLimit
        {
            AccountId = createDto.AccountId,
            CurrencyId = createDto.CurrencyId,
            BadehkarLimit = createDto.BadehkarLimit,
            CreatedBy = _context.RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        await _dbSet.AddAsync(entity);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(entity.Id))!;
    }

    public override async Task<AccountBadehkarLimitDto> UpdateAsync(
        long id,
        UpdateAccountBadehkarLimitDto updateDto)
    {
        var entity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null)
            throw new KeyNotFoundException($"محدودیت با شناسه {id} یافت نشد.");

        await ValidateTargetAsync(
            entity.AccountId,
            entity.CurrencyId,
            updateDto.BadehkarLimit);
        entity.BadehkarLimit = updateDto.BadehkarLimit;
        entity.IsActive = updateDto.IsActive;
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(id))!;
    }

    public async Task SetForAccountAsync(
        long accountId,
        IEnumerable<DebtLimitInputDto> limits)
    {
        var inputs = limits
            .GroupBy(x => x.CurrencyId)
            .Select(x => x.Last())
            .ToList();

        var account = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == accountId && !x.IsArchived)
            ?? throw new InvalidOperationException("حساب مشتری یا نمایندگی یافت نشد.");
        if (!IsEligibleAccount(account))
            throw new InvalidOperationException("سقف بدهکاری فقط برای حساب مشتری یا نمایندگی قابل تعریف است.");

        if (inputs.Any(x => x.CurrencyId <= 0 || x.BadehkarLimit < 0))
            throw new InvalidOperationException("ارز و مبلغ سقف بدهکاری معتبر نیست.");

        var currencyIds = inputs.Select(x => x.CurrencyId).Distinct().ToList();
        var validCurrencyIds = await _context.Currencies
            .AsNoTracking()
            .Where(x => currencyIds.Contains(x.Id) && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync();
        if (validCurrencyIds.Count != currencyIds.Count)
            throw new InvalidOperationException("یکی از ارزهای سقف بدهکاری معتبر یا فعال نیست.");

        var existing = await _dbSet.Where(x => x.AccountId == accountId).ToListAsync();
        var inputByCurrency = inputs.ToDictionary(x => x.CurrencyId);
        foreach (var entity in existing)
        {
            if (inputByCurrency.TryGetValue(entity.CurrencyId, out var input))
            {
                entity.BadehkarLimit = input.BadehkarLimit;
                entity.IsActive = input.IsEnabled;
            }
            else
            {
                entity.IsActive = false;
            }
        }

        var existingCurrencyIds = existing.Select(x => x.CurrencyId).ToHashSet();
        foreach (var input in inputs.Where(x => x.IsEnabled && !existingCurrencyIds.Contains(x.CurrencyId)))
        {
            _dbSet.Add(new AccountBadehkarLimit
            {
                AccountId = accountId,
                CurrencyId = input.CurrencyId,
                BadehkarLimit = input.BadehkarLimit,
                IsActive = true,
                CreatedBy = _context.RequireCurrentUserId(),
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    private async Task ValidateTargetAsync(
        long accountId,
        long currencyId,
        decimal limitAmount)
    {
        if (limitAmount < 0)
            throw new InvalidOperationException("سقف بدهکاری نمی‌تواند منفی باشد.");

        var account = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == accountId && !x.IsArchived);
        if (account == null)
            throw new InvalidOperationException("حساب انتخاب‌شده معتبر یا فعال نیست.");
        if (!IsEligibleAccount(account))
        {
            throw new InvalidOperationException(
                "سقف بدهکاری فقط برای حساب مشتری یا نمایندگی قابل تعریف است.");
        }

        var currencyExists = await _context.Currencies
            .AsNoTracking()
            .AnyAsync(x => x.Id == currencyId && x.IsActive);
        if (!currencyExists)
            throw new InvalidOperationException("ارز انتخاب‌شده معتبر یا فعال نیست.");
    }

    private IQueryable<AccountBadehkarLimit> LimitsQuery() =>
        _dbSet
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Include(x => x.CreatedByUser);

    private async Task<List<AccountBadehkarLimitDto>> ToDtosAsync(
        IReadOnlyCollection<AccountBadehkarLimit> entities)
    {
        if (entities.Count == 0)
            return [];

        var accountIds = entities.Select(x => x.AccountId).Distinct().ToList();
        var currencyIds = entities.Select(x => x.CurrencyId).Distinct().ToList();
        var balances = await _context.LedgerEntries
            .AsNoTracking()
            .Where(x =>
                accountIds.Contains(x.AccountId) &&
                currencyIds.Contains(x.CurrencyId))
            .GroupBy(x => new { x.AccountId, x.CurrencyId })
            .Select(group => new
            {
                group.Key.AccountId,
                group.Key.CurrencyId,
                Balance = group.Sum(x => x.TalabKar - x.BadehKar)
            })
            .ToListAsync();
        var balanceMap = balances.ToDictionary(
            x => (x.AccountId, x.CurrencyId),
            x => x.Balance);

        return entities.Select(entity =>
        {
            var balance = balanceMap.GetValueOrDefault(
                (entity.AccountId, entity.CurrencyId));
            var debt = Math.Max(-balance, 0m);
            return new AccountBadehkarLimitDto
            {
                Id = entity.Id,
                AccountId = entity.AccountId,
                AccountName = entity.Account?.AccountName ?? string.Empty,
                AccountType = GetAccountTypeLabel(entity.Account),
                CurrencyId = entity.CurrencyId,
                CurrencyCode = entity.Currency?.Code ?? string.Empty,
                BadehkarLimit = entity.BadehkarLimit,
                CurrentBalance = balance,
                CurrentDebt = debt,
                AvailableDebt = Math.Max(entity.BadehkarLimit - debt, 0m),
                IsOverLimit = debt > entity.BadehkarLimit,
                IsActive = entity.IsActive,
                CreatedBy = entity.CreatedBy,
                CreatedByName = entity.CreatedByUser?.FullName ?? "-",
                CreatedAt = entity.CreatedAt
            };
        }).ToList();
    }

    private static bool IsEligibleAccount(Account account) =>
        account.AccountType is "Customer" or "Correspondent" or "مشتری" or "نماینده" or "نمایندگی" ||
        account.CustomerId.HasValue || account.CorrespondentId.HasValue;

    private static string GetAccountTypeLabel(Account? account)
    {
        if (account == null)
            return "-";
        if (account.AccountType is "Correspondent" or "نماینده" or "نمایندگی" ||
            account.CorrespondentId.HasValue)
            return "نمایندگی";
        return "مشتری";
    }
}
