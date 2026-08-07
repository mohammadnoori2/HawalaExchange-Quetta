using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class TenantAdministrationService(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager) : ITenantAdministrationService
{
    public async Task<IReadOnlyList<TenantDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.Tenants
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new TenantDto
            {
                Id = x.Id,
                Name = x.Name,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UserCount = context.Users.IgnoreQueryFilters().Count(u => u.TenantId == x.Id),
                BranchCount = context.Branches.IgnoreQueryFilters().Count(b => b.TenantId == x.Id)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantDto> CreateAsync(
        CreateTenantDto dto,
        CancellationToken cancellationToken = default)
    {
        var localUserName = dto.AdminUserName.Trim();
        var normalizedEmail = userManager.NormalizeEmail(dto.AdminEmail.Trim());

        if (await context.Users.IgnoreQueryFilters()
            .AnyAsync(x => x.LocalUserName == localUserName, cancellationToken))
            throw new InvalidOperationException("این نام کاربری قبلاً در سیستم ثبت شده است.");

        if (await context.Users.IgnoreQueryFilters()
            .AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
            throw new InvalidOperationException("این ایمیل قبلاً در سیستم ثبت شده است.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tenant = new Tenant
        {
            Name = dto.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(cancellationToken);

        using (context.UseTenantScope(tenant.Id))
        {
            var branch = new Branch
            {
                TenantId = tenant.Id,
                Code = "MAIN",
                Name = "شعبه اصلی",
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            };
            context.Branches.Add(branch);

            context.Currencies.AddRange(CreateCurrencies(tenant.Id));
            context.Accounts.AddRange(CreateBaseAccounts(tenant.Id));
            await context.SaveChangesAsync(cancellationToken);

            var user = new ApplicationUser
            {
                TenantId = tenant.Id,
                BranchId = branch.Id,
                LocalUserName = localUserName,
                UserName = $"{tenant.Id}:{localUserName}",
                Email = dto.AdminEmail.Trim(),
                FullName = dto.AdminFullName.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, dto.AdminPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("؛ ", result.Errors.Select(x => x.Description)));

            result = await userManager.AddToRoleAsync(user, "Admin");
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("؛ ", result.Errors.Select(x => x.Description)));

            await context.EnsureSystemAccountsAsync(tenant.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            IsActive = tenant.IsActive,
            UserCount = 1,
            BranchCount = 1,
            CreatedAt = tenant.CreatedAt
        };
    }

    public async Task<TenantDto> UpdateAsync(
        long id,
        UpdateTenantDto dto,
        CancellationToken cancellationToken = default)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("صرافی مورد نظر یافت نشد.");

        if (!dto.IsActive && id == context.CurrentTenantId)
            throw new InvalidOperationException("صرافی‌ای را که اکنون با آن وارد شده‌اید نمی‌توانید غیرفعال کنید.");

        tenant.Name = dto.Name.Trim();
        tenant.IsActive = dto.IsActive;
        await context.SaveChangesAsync(cancellationToken);

        return new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            IsActive = tenant.IsActive,
            UserCount = await context.Users.IgnoreQueryFilters().CountAsync(x => x.TenantId == id, cancellationToken),
            BranchCount = await context.Branches.IgnoreQueryFilters().CountAsync(x => x.TenantId == id, cancellationToken),
            CreatedAt = tenant.CreatedAt
        };
    }

    private static Currency[] CreateCurrencies(long tenantId) =>
    [
        new() { TenantId = tenantId, Code = "AFN", Name = "افغانی", Symbol = "؋", QuotationPriority = 60 },
        new() { TenantId = tenantId, Code = "USD", Name = "دالر امریکایی", Symbol = "$", QuotationPriority = 20 },
        new() { TenantId = tenantId, Code = "EUR", Name = "یورو", Symbol = "€", QuotationPriority = 10 },
        new() { TenantId = tenantId, Code = "AED", Name = "درهم عربی", Symbol = "د.إ", QuotationPriority = 30 },
        new() { TenantId = tenantId, Code = "IRR", Name = "ریال ایرانی", Symbol = "﷼", QuotationPriority = 50 },
        new() { TenantId = tenantId, Code = "PKR", Name = "روپیه پاکستانی", Symbol = "₨", QuotationPriority = 40 }
    ];

    private static Account[] CreateBaseAccounts(long tenantId) =>
    [
        NewAccount(tenantId, "1001", "صندوق", "Cash"),
        NewAccount(tenantId, "1101", "بانک", "Bank"),
        NewAccount(tenantId, "3001", "درآمد کمیسیون حواله", "Income"),
        NewAccount(tenantId, "3002", "درآمد تبادل", "Income"),
        NewAccount(tenantId, "4001", "هزینه دفتر", "Expense"),
        NewAccount(tenantId, "5001", "سرمایه مالک", "Equity")
    ];

    private static Account NewAccount(long tenantId, string code, string name, string type) => new()
    {
        TenantId = tenantId,
        AccountCode = code,
        AccountName = name,
        AccountType = type,
        CreatedAt = DateTime.UtcNow
    };
}
