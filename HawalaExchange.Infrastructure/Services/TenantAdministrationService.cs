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
        var tenants = await context.Tenants
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                Tenant = x,
                UserCount = context.Users.IgnoreQueryFilters().Count(u => u.TenantId == x.Id && !u.IsPlatformUser),
                BranchCount = context.Branches.IgnoreQueryFilters().Count(b => b.TenantId == x.Id)
            })
            .ToListAsync(cancellationToken);

        var tenantIds = tenants.Select(x => x.Tenant.Id).ToArray();
        var admins = await (
                from user in context.Users.IgnoreQueryFilters().AsNoTracking()
                join userRole in context.Set<IdentityUserRole<long>>() on user.Id equals userRole.UserId
                join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where tenantIds.Contains(user.TenantId) && !user.IsPlatformUser && role.NormalizedName == "ADMIN"
                orderby user.CreatedAt, user.Id
                select user)
            .ToListAsync(cancellationToken);
        var adminByTenant = admins
            .GroupBy(x => x.TenantId)
            .ToDictionary(x => x.Key, x => x.First());
        var tenantsWithoutAdminRole = tenantIds.Except(adminByTenant.Keys).ToArray();
        if (tenantsWithoutAdminRole.Length > 0)
        {
            var fallbackAdmins = await context.Users.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => tenantsWithoutAdminRole.Contains(x.TenantId) && !x.IsPlatformUser)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var fallback in fallbackAdmins.GroupBy(x => x.TenantId).Select(x => x.First()))
                adminByTenant[fallback.TenantId] = fallback;
        }

        return tenants.Select(x =>
        {
            adminByTenant.TryGetValue(x.Tenant.Id, out var admin);
            return MapTenant(x.Tenant, admin, x.UserCount, x.BranchCount);
        }).ToList();
    }

    public async Task<TenantDto> CreateAsync(
        CreateTenantDto dto,
        CancellationToken cancellationToken = default)
    {
        var localUserName = dto.AdminUserName.Trim();
        var normalizedEmail = userManager.NormalizeEmail(dto.AdminEmail.Trim());

        if (dto.SubscriptionEndAt <= dto.SubscriptionStartAt)
            throw new InvalidOperationException("تاریخ پایان اشتراک باید بعد از تاریخ شروع باشد.");

        var plan = await context.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dto.PlanId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("پلن اشتراک انتخاب‌شده معتبر یا فعال نیست.");

        if (await context.Users.IgnoreQueryFilters()
            .AnyAsync(x => x.LocalUserName == localUserName, cancellationToken))
            throw new InvalidOperationException("این نام کاربری قبلاً در سیستم ثبت شده است.");

        if (await context.Users.IgnoreQueryFilters()
            .AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
            throw new InvalidOperationException("این ایمیل قبلاً در سیستم ثبت شده است.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        using var subscriptionBypass = context.BypassSubscriptionEnforcement();
        var tenant = new Tenant
        {
            Name = dto.Name.Trim(),
            LegalName = dto.LegalName?.Trim(),
            ContactName = dto.ContactName?.Trim(),
            ContactEmail = dto.ContactEmail?.Trim(),
            ContactPhone = dto.ContactPhone?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(cancellationToken);

        ApplicationUser? createdAdmin = null;
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
            createdAdmin = user;

            result = await userManager.AddToRoleAsync(user, "Admin");
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("؛ ", result.Errors.Select(x => x.Description)));

            await context.EnsureSystemAccountsAsync(tenant.Id, cancellationToken);

            var startAt = DateTime.SpecifyKind(dto.SubscriptionStartAt, DateTimeKind.Utc);
            context.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = tenant.Id,
                PlanId = plan.Id,
                Status = plan.TrialDays > 0 ? SubscriptionStatus.Trial : SubscriptionStatus.Active,
                BillingCycle = dto.BillingCycle,
                StartAt = startAt,
                EndAt = DateTime.SpecifyKind(dto.SubscriptionEndAt, DateTimeKind.Utc),
                TrialEndAt = plan.TrialDays > 0 ? startAt.AddDays(plan.TrialDays) : null,
                AutoRenew = dto.AutoRenew,
                AgreedPrice = dto.AgreedPrice,
                CurrencyCode = dto.SubscriptionCurrencyCode.Trim().ToUpperInvariant(),
                NextPaymentAt = startAt,
                CreatedAt = DateTime.UtcNow
            });
            context.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                ActorUserId = context.CurrentUserId > 0 ? context.CurrentUserId : null,
                TenantId = tenant.Id,
                Action = "CREATE_TENANT",
                EntityName = nameof(Tenant),
                EntityId = tenant.Id,
                Details = $"Tenant={tenant.Name}; Plan={plan.Name}",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            LegalName = tenant.LegalName,
            ContactName = tenant.ContactName,
            ContactEmail = tenant.ContactEmail,
            ContactPhone = tenant.ContactPhone,
            AdminUserId = createdAdmin!.Id,
            AdminFullName = createdAdmin.FullName,
            AdminUserName = createdAdmin.LocalUserName,
            AdminEmail = createdAdmin.Email ?? string.Empty,
            IsActive = tenant.IsActive,
            IsArchived = tenant.IsArchived,
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

        var admin = await context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == dto.AdminUserId && x.TenantId == id && !x.IsPlatformUser, cancellationToken)
            ?? throw new InvalidOperationException("مدیر صرافی معتبر نیست.");
        var localUserName = dto.AdminUserName.Trim();
        var email = dto.AdminEmail.Trim();
        var normalizedEmail = userManager.NormalizeEmail(email);
        if (await context.Users.IgnoreQueryFilters().AnyAsync(
                x => x.Id != admin.Id && x.LocalUserName == localUserName, cancellationToken))
            throw new InvalidOperationException("این نام کاربری قبلاً در سیستم ثبت شده است.");
        if (await context.Users.IgnoreQueryFilters().AnyAsync(
                x => x.Id != admin.Id && x.NormalizedEmail == normalizedEmail, cancellationToken))
            throw new InvalidOperationException("این ایمیل قبلاً در سیستم ثبت شده است.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        using var tenantScope = context.UseTenantScope(id);
        using var subscriptionBypass = context.BypassSubscriptionEnforcement();
        tenant.Name = dto.Name.Trim();
        tenant.LegalName = NullIfWhiteSpace(dto.LegalName);
        tenant.ContactName = NullIfWhiteSpace(dto.ContactName);
        tenant.ContactEmail = NullIfWhiteSpace(dto.ContactEmail);
        tenant.ContactPhone = NullIfWhiteSpace(dto.ContactPhone);
        tenant.IsActive = dto.IsActive;

        admin.FullName = dto.AdminFullName.Trim();
        admin.LocalUserName = localUserName;
        admin.UserName = $"{tenant.Id}:{localUserName}";
        admin.NormalizedUserName = userManager.NormalizeName(admin.UserName);
        admin.Email = email;
        admin.NormalizedEmail = normalizedEmail;

        if (!string.IsNullOrWhiteSpace(dto.NewAdminPassword))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(admin);
            var passwordResult = await userManager.ResetPasswordAsync(admin, token, dto.NewAdminPassword);
            if (!passwordResult.Succeeded)
                throw new InvalidOperationException(string.Join("؛ ", passwordResult.Errors.Select(x => x.Description)));
        }

        context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            ActorUserId = context.CurrentUserId > 0 ? context.CurrentUserId : null,
            TenantId = tenant.Id,
            Action = "UPDATE_TENANT",
            EntityName = nameof(Tenant),
            EntityId = tenant.Id,
            Details = $"مشخصات صرافی {tenant.Name} و مدیر آن ویرایش شد.",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapTenant(
            tenant,
            admin,
            await context.Users.IgnoreQueryFilters().CountAsync(x => x.TenantId == id && !x.IsPlatformUser, cancellationToken),
            await context.Branches.IgnoreQueryFilters().CountAsync(x => x.TenantId == id, cancellationToken));
    }

    public async Task ArchiveAsync(long id, CancellationToken cancellationToken = default)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("صرافی مورد نظر یافت نشد.");

        if (id == context.CurrentTenantId)
            throw new InvalidOperationException("صرافی جاری را نمی‌توانید بایگانی کنید.");

        tenant.IsArchived = true;
        tenant.IsActive = false;
        context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            ActorUserId = context.CurrentUserId > 0 ? context.CurrentUserId : null,
            TenantId = tenant.Id,
            Action = "ARCHIVE_TENANT",
            EntityName = nameof(Tenant),
            EntityId = tenant.Id,
            Details = $"صرافی {tenant.Name} بایگانی شد.",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UnarchiveAsync(long id, CancellationToken cancellationToken = default)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("صرافی مورد نظر یافت نشد.");

        tenant.IsArchived = false;
        context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            ActorUserId = context.CurrentUserId > 0 ? context.CurrentUserId : null,
            TenantId = tenant.Id,
            Action = "UNARCHIVE_TENANT",
            EntityName = nameof(Tenant),
            EntityId = tenant.Id,
            Details = $"صرافی {tenant.Name} از بایگانی خارج شد.",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
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

    private static TenantDto MapTenant(Tenant tenant, ApplicationUser? admin, int userCount, int branchCount) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        LegalName = tenant.LegalName,
        ContactName = tenant.ContactName,
        ContactEmail = tenant.ContactEmail,
        ContactPhone = tenant.ContactPhone,
        AdminUserId = admin?.Id,
        AdminFullName = admin?.FullName ?? string.Empty,
        AdminUserName = admin?.LocalUserName ?? string.Empty,
        AdminEmail = admin?.Email ?? string.Empty,
        IsActive = tenant.IsActive,
        IsArchived = tenant.IsArchived,
        UserCount = userCount,
        BranchCount = branchCount,
        CreatedAt = tenant.CreatedAt
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
