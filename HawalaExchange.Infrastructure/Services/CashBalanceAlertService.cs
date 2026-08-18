using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class CashBalanceAlertService(
    ApplicationDbContext context,
    IDbContextFactory<ApplicationDbContext> contextFactory) : ICashBalanceAlertService
{
    public event Action? AlertsChanged
    {
        add => context.CashBalanceAlertsChanged += value;
        remove => context.CashBalanceAlertsChanged -= value;
    }

    public async Task<IReadOnlyList<CashBalanceAlertSettingDto>> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await SettingsQuery()
            .OrderBy(x => x.Account.AccountName)
            .ThenBy(x => x.Currency.Code)
            .ToListAsync(cancellationToken);
        return await MapSettingsAsync(settings, cancellationToken);
    }

    public async Task<CashBalanceAlertSettingDto?> GetSettingAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var setting = await SettingsQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return setting == null
            ? null
            : (await MapSettingsAsync([setting], cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<CashBalanceAlertDto>> GetActiveAlertsAsync(
        bool onlyForCurrentUser = true,
        CancellationToken cancellationToken = default)
    {
        await using var readContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var userId = context.CurrentUserId;
        var query = readContext.CashBalanceAlerts
            .AsNoTracking()
            .Where(x => x.IsActive && x.Setting.IsActive && x.Setting.ShowInApp);

        if (onlyForCurrentUser)
        {
            if (userId <= 0)
                return [];
            query = query.Where(x =>
                x.Setting.NotifyAllUsers ||
                x.Setting.Recipients.Any(recipient => recipient.UserId == userId));
        }

        return await query
            .OrderByDescending(x => x.TriggeredAt)
            .Select(x => new CashBalanceAlertDto
            {
                Id = x.Id,
                SettingId = x.SettingId,
                AccountName = x.Setting.Account.AccountName,
                CurrencyCode = x.Setting.Currency.Code,
                CurrentBalance = x.CurrentBalance,
                MinimumBalance = x.MinimumBalance,
                TriggeredAt = x.TriggeredAt,
                LastCheckedAt = x.LastCheckedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashBalanceAlertUserDto>> GetRecipientUsersAsync(
        CancellationToken cancellationToken = default) =>
        await context.Users
            .AsNoTracking()
            .Where(x => !x.IsPlatformUser && x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new CashBalanceAlertUserDto
            {
                Id = x.Id,
                FullName = x.FullName,
                UserName = x.LocalUserName
            })
            .ToListAsync(cancellationToken);

    public async Task<CashBalanceAlertSettingDto> CreateAsync(
        SaveCashBalanceAlertSettingDto dto,
        CancellationToken cancellationToken = default)
    {
        await ValidateAsync(dto, cancellationToken);
        if (await context.CashBalanceAlertSettings.AnyAsync(x =>
                x.AccountId == dto.AccountId && x.CurrencyId == dto.CurrencyId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "برای این صندوق و ارز قبلاً حداقل موجودی تعیین شده است؛ همان مورد را ویرایش کنید.");
        }

        var now = DateTime.UtcNow;
        var entity = new CashBalanceAlertSetting
        {
            AccountId = dto.AccountId,
            CurrencyId = dto.CurrencyId,
            MinimumBalance = dto.MinimumBalance,
            IsActive = dto.IsActive,
            NotifyAllUsers = dto.NotifyAllUsers,
            ShowInApp = dto.ShowInApp,
            CreatedBy = context.RequireCurrentUserId(),
            CreatedAt = now,
            UpdatedAt = now
        };
        AddRecipients(entity, dto.RecipientUserIds);
        context.CashBalanceAlertSettings.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return (await GetSettingAsync(entity.Id, cancellationToken))!;
    }

    public async Task<CashBalanceAlertSettingDto> UpdateAsync(
        long id,
        SaveCashBalanceAlertSettingDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.CashBalanceAlertSettings
            .Include(x => x.Recipients)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("تنظیم هشدار موجودی پیدا نشد.");

        if (entity.AccountId != dto.AccountId || entity.CurrencyId != dto.CurrencyId)
            throw new InvalidOperationException("صندوق و ارز قابل تغییر نیست؛ برای مورد جدید یک تنظیم تازه بسازید.");

        await ValidateAsync(dto, cancellationToken);
        entity.MinimumBalance = dto.MinimumBalance;
        entity.IsActive = dto.IsActive;
        entity.NotifyAllUsers = dto.NotifyAllUsers;
        entity.ShowInApp = dto.ShowInApp;
        entity.UpdatedAt = DateTime.UtcNow;
        context.CashBalanceAlertRecipients.RemoveRange(entity.Recipients);
        entity.Recipients.Clear();
        AddRecipients(entity, dto.RecipientUserIds);
        await context.SaveChangesAsync(cancellationToken);
        return (await GetSettingAsync(entity.Id, cancellationToken))!;
    }

    public async Task SetActiveAsync(
        long id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.CashBalanceAlertSettings.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("تنظیم هشدار موجودی پیدا نشد.");
        entity.IsActive = isActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<CashBalanceAlertSetting> SettingsQuery() =>
        context.CashBalanceAlertSettings
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Currency)
            .Include(x => x.Recipients)
                .ThenInclude(x => x.User);

    private async Task<IReadOnlyList<CashBalanceAlertSettingDto>> MapSettingsAsync(
        IReadOnlyCollection<CashBalanceAlertSetting> settings,
        CancellationToken cancellationToken)
    {
        if (settings.Count == 0)
            return [];

        var accountIds = settings.Select(x => x.AccountId).Distinct().ToArray();
        var currencyIds = settings.Select(x => x.CurrencyId).Distinct().ToArray();
        var balances = await context.LedgerEntries
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && currencyIds.Contains(x.CurrencyId))
            .GroupBy(x => new { x.AccountId, x.CurrencyId })
            .Select(group => new
            {
                group.Key.AccountId,
                group.Key.CurrencyId,
                Balance = group.Sum(x => x.BadehKar - x.TalabKar)
            })
            .ToDictionaryAsync(x => (x.AccountId, x.CurrencyId), x => x.Balance, cancellationToken);

        return settings.Select(x => new CashBalanceAlertSettingDto
        {
            Id = x.Id,
            AccountId = x.AccountId,
            AccountName = x.Account.AccountName,
            CurrencyId = x.CurrencyId,
            CurrencyCode = x.Currency.Code,
            MinimumBalance = x.MinimumBalance,
            CurrentBalance = balances.GetValueOrDefault((x.AccountId, x.CurrencyId)),
            IsActive = x.IsActive,
            NotifyAllUsers = x.NotifyAllUsers,
            ShowInApp = x.ShowInApp,
            RecipientUserIds = x.Recipients.Select(recipient => recipient.UserId).ToList(),
            RecipientNames = x.Recipients.Select(recipient => recipient.User.FullName).ToList(),
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList();
    }

    private async Task ValidateAsync(
        SaveCashBalanceAlertSettingDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.MinimumBalance < 0)
            throw new InvalidOperationException("حداقل موجودی نمی‌تواند منفی باشد.");

        var accountExists = await context.Accounts.AsNoTracking().AnyAsync(x =>
            x.Id == dto.AccountId && x.AccountType == "Cash" && !x.IsArchived,
            cancellationToken);
        if (!accountExists)
            throw new InvalidOperationException("صندوق انتخاب‌شده معتبر یا فعال نیست.");

        if (!await context.Currencies.AsNoTracking().AnyAsync(x => x.Id == dto.CurrencyId && x.IsActive, cancellationToken))
            throw new InvalidOperationException("ارز انتخاب‌شده معتبر یا فعال نیست.");

        if (!dto.NotifyAllUsers)
        {
            var selectedIds = dto.RecipientUserIds.Distinct().ToArray();
            if (selectedIds.Length == 0)
                throw new InvalidOperationException("حداقل یک دریافت‌کنندهٔ هشدار را انتخاب کنید.");
            var validCount = await context.Users.CountAsync(x =>
                selectedIds.Contains(x.Id) && !x.IsPlatformUser && x.IsActive,
                cancellationToken);
            if (validCount != selectedIds.Length)
                throw new InvalidOperationException("یک یا چند کاربر انتخاب‌شده معتبر نیست.");
        }
    }

    private static void AddRecipients(CashBalanceAlertSetting setting, IEnumerable<long> userIds)
    {
        if (setting.NotifyAllUsers)
            return;
        foreach (var userId in userIds.Distinct())
            setting.Recipients.Add(new CashBalanceAlertRecipient { UserId = userId });
    }
}
