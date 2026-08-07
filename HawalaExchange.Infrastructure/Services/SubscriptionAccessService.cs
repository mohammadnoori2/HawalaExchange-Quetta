using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class SubscriptionAccessService(ApplicationDbContext context) : ISubscriptionAccessService
{
    public async Task<SubscriptionAccessDto> GetAccessAsync(long tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var subscription = await context.TenantSubscriptions
            .AsNoTracking()
            .Include(x => x.Plan)
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.StartAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
            return Blocked(tenantId, "برای این صرافی اشتراک فعالی ثبت نشده است.");

        var result = new SubscriptionAccessDto
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            Status = subscription.Status,
            PlanName = subscription.Plan.Name,
            EndAt = subscription.EndAt,
            GracePeriodEndAt = subscription.GracePeriodEndAt
        };

        if (subscription.Status is SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled)
        {
            result.AccessLevel = SubscriptionAccessLevel.Blocked;
            result.Message = subscription.Status == SubscriptionStatus.Suspended
                ? "اشتراک این صرافی تعلیق شده است."
                : "اشتراک این صرافی لغو شده است.";
            return result;
        }

        if (subscription.EndAt >= now)
        {
            result.AccessLevel = SubscriptionAccessLevel.Full;
            result.Message = subscription.Status == SubscriptionStatus.ExpiringSoon
                ? $"اشتراک تا {subscription.EndAt:yyyy/MM/dd} اعتبار دارد و نزدیک انقضا است."
                : "اشتراک فعال است.";
            return result;
        }

        if (subscription.GracePeriodEndAt is { } graceEnd && graceEnd >= now)
        {
            result.AccessLevel = SubscriptionAccessLevel.ReadOnly;
            result.Message = $"اشتراک منقضی شده و سیستم تا {graceEnd:yyyy/MM/dd} فقط خواندنی است.";
            return result;
        }

        result.AccessLevel = SubscriptionAccessLevel.Blocked;
        result.Message = "اشتراک و دوره مهلت پایان یافته است. برای ادامه، اشتراک را تمدید کنید.";
        return result;
    }

    public async Task EnsureCanSignInAsync(long tenantId, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(tenantId, cancellationToken);
        if (!access.CanSignIn) throw new InvalidOperationException(access.Message);
    }

    public async Task EnsureCanWriteAsync(long tenantId, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(tenantId, cancellationToken);
        if (!access.CanWrite) throw new InvalidOperationException(access.Message);
    }

    public async Task EnsureUserCapacityAsync(long tenantId, CancellationToken cancellationToken = default)
    {
        var plan = await GetWritablePlanAsync(tenantId, cancellationToken);
        if (plan.MaxUsers <= 0) return;
        var count = await context.Users.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenantId && !x.IsPlatformUser && x.IsActive, cancellationToken);
        if (count >= plan.MaxUsers)
            throw new InvalidOperationException($"حداکثر کاربران پلن ({plan.MaxUsers}) تکمیل شده است.");
    }

    public async Task EnsureBranchCapacityAsync(long tenantId, CancellationToken cancellationToken = default)
    {
        var plan = await GetWritablePlanAsync(tenantId, cancellationToken);
        if (plan.MaxBranches <= 0) return;
        var count = await context.Branches.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenantId && !x.IsArchived, cancellationToken);
        if (count >= plan.MaxBranches)
            throw new InvalidOperationException($"حداکثر شعبه‌های پلن ({plan.MaxBranches}) تکمیل شده است.");
    }

    public async Task EnsureMonthlyTransactionCapacityAsync(long tenantId, CancellationToken cancellationToken = default)
    {
        var plan = await GetWritablePlanAsync(tenantId, cancellationToken);
        if (plan.MaxMonthlyTransactions <= 0) return;
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var count = await context.Transactions.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenantId && x.CreatedAt >= monthStart, cancellationToken);
        if (count >= plan.MaxMonthlyTransactions)
            throw new InvalidOperationException($"سقف تراکنش ماهانه پلن ({plan.MaxMonthlyTransactions}) تکمیل شده است.");
    }

    public async Task EnsureDocumentCapacityAsync(long tenantId, long incomingBytes, CancellationToken cancellationToken = default)
    {
        var plan = await GetWritablePlanAsync(tenantId, cancellationToken);
        if (!plan.IncludesDocumentManagement)
            throw new InvalidOperationException("مدیریت اسناد در پلن فعلی فعال نیست.");
        if (plan.MaxStorageBytes <= 0) return;
        var used = await context.Documents.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .SumAsync(x => (long?)x.FileSizeBytes, cancellationToken) ?? 0;
        if (used + incomingBytes > plan.MaxStorageBytes)
            throw new InvalidOperationException("فضای ذخیره‌سازی پلن تکمیل شده است.");
    }

    private async Task<SubscriptionPlan> GetWritablePlanAsync(long tenantId, CancellationToken cancellationToken)
    {
        await EnsureCanWriteAsync(tenantId, cancellationToken);
        return await context.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.StartAt).ThenByDescending(x => x.Id)
            .Select(x => x.Plan)
            .FirstAsync(cancellationToken);
    }

    private static SubscriptionAccessDto Blocked(long tenantId, string message) => new()
    {
        TenantId = tenantId,
        AccessLevel = SubscriptionAccessLevel.Blocked,
        Message = message
    };
}
