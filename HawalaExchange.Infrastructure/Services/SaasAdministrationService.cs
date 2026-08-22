using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class SaasAdministrationService(ApplicationDbContext context) : ISaasAdministrationService
{
    public async Task<SaasDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await RefreshSubscriptionStatusesAsync(cancellationToken);
        var tenants = await GetTenantsAsync(cancellationToken: cancellationToken);
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return new SaasDashboardDto
        {
            TotalTenants = tenants.Count,
            ActiveTenants = tenants.Count(x => x.SubscriptionStatus == SubscriptionStatus.Active),
            TrialTenants = tenants.Count(x => x.SubscriptionStatus == SubscriptionStatus.Trial),
            SuspendedTenants = tenants.Count(x => x.SubscriptionStatus == SubscriptionStatus.Suspended),
            ExpiredTenants = tenants.Count(x => x.SubscriptionStatus == SubscriptionStatus.Expired),
            ExpiringSoonTenants = tenants.Count(x => x.SubscriptionStatus == SubscriptionStatus.ExpiringSoon),
            TotalUsers = await context.Users.IgnoreQueryFilters().CountAsync(x => !x.IsPlatformUser, cancellationToken),
            CollectedRevenueThisMonth = await context.SubscriptionPayments
                .Where(x => x.Status == SubscriptionPaymentStatus.Paid && x.PaidAt >= monthStart)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0,
            RecentTenants = tenants.OrderByDescending(x => x.CreatedAt).Take(8).ToList()
        };
    }

    public async Task<IReadOnlyList<SaasTenantDto>> GetTenantsAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.Tenants.AsNoTracking();
        if (!includeArchived)
            query = query.Where(x => !x.IsArchived);

        var rows = await query
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                Tenant = x,
                UserCount = context.Users.IgnoreQueryFilters().Count(u => u.TenantId == x.Id && !u.IsPlatformUser),
                BranchCount = context.Branches.IgnoreQueryFilters().Count(b => b.TenantId == x.Id),
                Subscription = x.Subscriptions
                    .OrderByDescending(s => s.StartAt)
                    .ThenByDescending(s => s.Id)
                    .Select(s => new
                    {
                        s.Id,
                        s.PlanId,
                        PlanName = s.Plan.Name,
                        s.Status,
                        s.StartAt,
                        s.EndAt,
                        s.TrialEndAt,
                        s.GracePeriodEndAt,
                        s.BillingCycle,
                        s.AutoRenew,
                        s.AgreedPrice,
                        s.CurrencyCode,
                        s.AdministrativeNote,
                        s.SuspensionReason
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        return rows.Select(x => new SaasTenantDto
        {
            Id = x.Tenant.Id,
            Name = x.Tenant.Name,
            ContactName = x.Tenant.ContactName,
            ContactEmail = x.Tenant.ContactEmail,
            ContactPhone = x.Tenant.ContactPhone,
            IsActive = x.Tenant.IsActive,
            IsArchived = x.Tenant.IsArchived,
            CreatedAt = x.Tenant.CreatedAt,
            LastActivityAt = x.Tenant.LastActivityAt,
            UserCount = x.UserCount,
            BranchCount = x.BranchCount,
            SubscriptionId = x.Subscription?.Id,
            PlanId = x.Subscription?.PlanId,
            PlanName = x.Subscription?.PlanName,
            SubscriptionStatus = x.Subscription?.Status,
            SubscriptionStartAt = x.Subscription?.StartAt,
            SubscriptionEndAt = x.Subscription?.EndAt,
            TrialEndAt = x.Subscription?.TrialEndAt,
            GracePeriodEndAt = x.Subscription?.GracePeriodEndAt,
            BillingCycle = x.Subscription?.BillingCycle,
            AutoRenew = x.Subscription?.AutoRenew ?? false,
            AgreedPrice = x.Subscription?.AgreedPrice ?? 0,
            CurrencyCode = x.Subscription?.CurrencyCode,
            AdministrativeNote = x.Subscription?.AdministrativeNote,
            SuspensionReason = x.Subscription?.SuspensionReason,
            RemainingDays = x.Subscription is null
                ? null
                : Math.Max(0, (int)Math.Ceiling((x.Subscription.EndAt - now).TotalDays))
        }).ToList();
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var query = context.SubscriptionPlans.AsNoTracking();
        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        return await query
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new SubscriptionPlanDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                MonthlyPrice = x.MonthlyPrice,
                AnnualPrice = x.AnnualPrice,
                CurrencyCode = x.CurrencyCode,
                TrialDays = x.TrialDays,
                MaxUsers = x.MaxUsers,
                MaxBranches = x.MaxBranches,
                MaxStorageBytes = x.MaxStorageBytes,
                MaxMonthlyTransactions = x.MaxMonthlyTransactions,
                IncludesAdvancedReports = x.IncludesAdvancedReports,
                IncludesDocumentManagement = x.IncludesDocumentManagement,
                IsActive = x.IsActive,
                DisplayOrder = x.DisplayOrder
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionPlanDto> CreatePlanAsync(
        SaveSubscriptionPlanDto dto,
        CancellationToken cancellationToken = default)
    {
        var code = dto.Code.Trim().ToUpperInvariant();
        if (await context.SubscriptionPlans.AnyAsync(x => x.Code == code, cancellationToken))
            throw new InvalidOperationException("این کد پلن قبلاً ثبت شده است.");

        var entity = new SubscriptionPlan { CreatedAt = DateTime.UtcNow };
        ApplyPlan(entity, dto, code);
        context.SubscriptionPlans.Add(entity);
        AddAudit("CREATE_PLAN", nameof(SubscriptionPlan), null, entity.Name);
        await context.SaveChangesAsync(cancellationToken);
        return MapPlan(entity);
    }

    public async Task<SubscriptionPlanDto> UpdatePlanAsync(
        long id,
        SaveSubscriptionPlanDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.SubscriptionPlans.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException("پلن مورد نظر یافت نشد.");
        var code = dto.Code.Trim().ToUpperInvariant();
        if (await context.SubscriptionPlans.AnyAsync(x => x.Id != id && x.Code == code, cancellationToken))
            throw new InvalidOperationException("این کد پلن قبلاً ثبت شده است.");

        ApplyPlan(entity, dto, code);
        entity.UpdatedAt = DateTime.UtcNow;
        AddAudit("UPDATE_PLAN", nameof(SubscriptionPlan), id, entity.Name);
        await context.SaveChangesAsync(cancellationToken);
        return MapPlan(entity);
    }

    public async Task ChangeSubscriptionAsync(
        long tenantId,
        ChangeSubscriptionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.EndAt <= dto.StartAt)
            throw new InvalidOperationException("تاریخ پایان اشتراک باید بعد از تاریخ شروع باشد.");

        var tenantExists = await context.Tenants.AnyAsync(x => x.Id == tenantId && !x.IsArchived, cancellationToken);
        if (!tenantExists)
            throw new KeyNotFoundException("صرافی مورد نظر یافت نشد.");
        if (!await context.SubscriptionPlans.AnyAsync(x => x.Id == dto.PlanId && x.IsActive, cancellationToken))
            throw new InvalidOperationException("پلن انتخاب‌شده معتبر یا فعال نیست.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        using var tenantScope = context.UseTenantScope(tenantId);
        using var subscriptionBypass = context.BypassSubscriptionEnforcement();
        var currentSubscriptions = await context.TenantSubscriptions
            .Where(x => x.TenantId == tenantId &&
                (x.Status == SubscriptionStatus.Active ||
                 x.Status == SubscriptionStatus.Trial ||
                 x.Status == SubscriptionStatus.ExpiringSoon ||
                 x.Status == SubscriptionStatus.Suspended))
            .ToListAsync(cancellationToken);
        foreach (var subscription in currentSubscriptions)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.UpdatedAt = DateTime.UtcNow;
        }

        var entity = new TenantSubscription
        {
            TenantId = tenantId,
            PlanId = dto.PlanId,
            Status = dto.Status,
            BillingCycle = dto.BillingCycle,
            StartAt = DateTime.SpecifyKind(dto.StartAt, DateTimeKind.Utc),
            EndAt = DateTime.SpecifyKind(dto.EndAt, DateTimeKind.Utc),
            TrialEndAt = ToUtc(dto.TrialEndAt),
            GracePeriodEndAt = ToUtc(dto.GracePeriodEndAt),
            AutoRenew = dto.AutoRenew,
            AgreedPrice = dto.AgreedPrice,
            CurrencyCode = dto.CurrencyCode.Trim().ToUpperInvariant(),
            NextPaymentAt = DateTime.SpecifyKind(dto.StartAt, DateTimeKind.Utc),
            AdministrativeNote = dto.AdministrativeNote?.Trim(),
            SuspensionReason = dto.SuspensionReason?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        context.TenantSubscriptions.Add(entity);
        AddAudit("CHANGE_SUBSCRIPTION", nameof(TenantSubscription), null, $"Tenant={tenantId}; Plan={dto.PlanId}; Status={dto.Status}", tenantId);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordPaymentAsync(
        RecordSubscriptionPaymentDto dto,
        CancellationToken cancellationToken = default)
    {
        var subscription = await context.TenantSubscriptions.FindAsync([dto.SubscriptionId], cancellationToken)
            ?? throw new KeyNotFoundException("اشتراک مورد نظر یافت نشد.");

        var payment = new SubscriptionPayment
        {
            SubscriptionId = subscription.Id,
            Amount = dto.Amount,
            CurrencyCode = dto.CurrencyCode.Trim().ToUpperInvariant(),
            Status = dto.Status,
            DueAt = DateTime.SpecifyKind(dto.DueAt, DateTimeKind.Utc),
            PaidAt = ToUtc(dto.PaidAt),
            PaymentMethod = dto.PaymentMethod?.Trim(),
            ReferenceNumber = dto.ReferenceNumber?.Trim(),
            Note = dto.Note?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        context.SubscriptionPayments.Add(payment);
        if (payment.Status == SubscriptionPaymentStatus.Paid)
            subscription.LastPaymentAt = payment.PaidAt ?? DateTime.UtcNow;

        AddAudit("RECORD_PAYMENT", nameof(SubscriptionPayment), null, $"Subscription={subscription.Id}; Amount={payment.Amount}", subscription.TenantId);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshSubscriptionStatusesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var warningDate = now.AddDays(30);
        var subscriptions = await context.TenantSubscriptions
            .Where(x => x.Status != SubscriptionStatus.Cancelled && x.Status != SubscriptionStatus.Suspended)
            .ToListAsync(cancellationToken);

        foreach (var item in subscriptions)
        {
            var newStatus = item.TrialEndAt > now
                ? SubscriptionStatus.Trial
                : item.EndAt <= now
                    ? SubscriptionStatus.Expired
                    : item.EndAt <= warningDate
                        ? SubscriptionStatus.ExpiringSoon
                        : SubscriptionStatus.Active;
            if (item.Status != newStatus)
            {
                item.Status = newStatus;
                item.UpdatedAt = now;
            }
        }

        if (context.ChangeTracker.HasChanges())
            await context.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(string action, string entityName, long? entityId, string? details, long? tenantId = null)
    {
        context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            ActorUserId = context.CurrentUserId > 0 ? context.CurrentUserId : null,
            TenantId = tenantId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void ApplyPlan(SubscriptionPlan entity, SaveSubscriptionPlanDto dto, string code)
    {
        entity.Code = code;
        entity.Name = dto.Name.Trim();
        entity.Description = dto.Description?.Trim();
        entity.MonthlyPrice = dto.MonthlyPrice;
        entity.AnnualPrice = dto.AnnualPrice;
        entity.CurrencyCode = dto.CurrencyCode.Trim().ToUpperInvariant();
        entity.TrialDays = dto.TrialDays;
        entity.MaxUsers = dto.MaxUsers;
        entity.MaxBranches = dto.MaxBranches;
        entity.MaxStorageBytes = dto.MaxStorageBytes;
        entity.MaxMonthlyTransactions = dto.MaxMonthlyTransactions;
        entity.IncludesAdvancedReports = dto.IncludesAdvancedReports;
        entity.IncludesDocumentManagement = dto.IncludesDocumentManagement;
        entity.IsActive = dto.IsActive;
        entity.DisplayOrder = dto.DisplayOrder;
    }

    private static SubscriptionPlanDto MapPlan(SubscriptionPlan x) => new()
    {
        Id = x.Id,
        Code = x.Code,
        Name = x.Name,
        Description = x.Description,
        MonthlyPrice = x.MonthlyPrice,
        AnnualPrice = x.AnnualPrice,
        CurrencyCode = x.CurrencyCode,
        TrialDays = x.TrialDays,
        MaxUsers = x.MaxUsers,
        MaxBranches = x.MaxBranches,
        MaxStorageBytes = x.MaxStorageBytes,
        MaxMonthlyTransactions = x.MaxMonthlyTransactions,
        IncludesAdvancedReports = x.IncludesAdvancedReports,
        IncludesDocumentManagement = x.IncludesDocumentManagement,
        IsActive = x.IsActive,
        DisplayOrder = x.DisplayOrder
    };

    private static DateTime? ToUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
