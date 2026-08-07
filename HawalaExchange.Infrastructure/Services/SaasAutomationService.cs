using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class SaasAutomationService(
    ApplicationDbContext context,
    ISaasBillingService billingService,
    IPlatformMessageSender messageSender) : ISaasAutomationService
{
    public async Task<SaasAutomationDashboardDto> GetDashboardAsync(string? search = null, SaasNotificationType? type = null, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var query = context.SaasNotifications.AsNoTracking().Include(x => x.Tenant).AsQueryable();
        if (type.HasValue) query = query.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Title.Contains(term) || x.Message.Contains(term) || (x.Tenant != null && x.Tenant.Name.Contains(term))); }
        var notifications = await query.OrderByDescending(x => x.CreatedAt).Take(200).Select(x => new SaasNotificationDto
        {
            Id = x.Id, TenantName = x.Tenant == null ? "پلتفرم" : x.Tenant.Name, Type = x.Type, Severity = x.Severity,
            Title = x.Title, Message = x.Message, DeliveryStatus = x.DeliveryStatus, CreatedAt = x.CreatedAt, ReadAt = x.ReadAt
        }).ToListAsync(cancellationToken);
        var runs = await context.SaasAutomationRuns.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(20).Select(x => new SaasAutomationRunDto
        {
            Id = x.Id, RunKey = x.RunKey, Status = x.Status, StartedAt = x.StartedAt, CompletedAt = x.CompletedAt,
            ProcessedSubscriptions = x.ProcessedSubscriptions, CreatedInvoices = x.CreatedInvoices, CreatedNotifications = x.CreatedNotifications, Error = x.Error
        }).ToListAsync(cancellationToken);
        return new SaasAutomationDashboardDto
        {
            Settings = Map(settings), Notifications = notifications, Runs = runs,
            UnreadCount = await context.SaasNotifications.CountAsync(x => x.ReadAt == null, cancellationToken),
            CriticalCount = await context.SaasNotifications.CountAsync(x => x.ReadAt == null && x.Severity == SaasNotificationSeverity.Critical, cancellationToken)
        };
    }

    public async Task<SaasAutomationSettingsDto> SaveSettingsAsync(SaasAutomationSettingsDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await GetSettingsAsync(cancellationToken);
        entity.IsEnabled = dto.IsEnabled; entity.AutoCreateInvoices = dto.AutoCreateInvoices; entity.SendEmailNotifications = dto.SendEmailNotifications;
        entity.RunIntervalMinutes = dto.RunIntervalMinutes; entity.ExpiryWarningDays = dto.ExpiryWarningDays; entity.GracePeriodDays = dto.GracePeriodDays;
        entity.GraceWarningDays = dto.GraceWarningDays; entity.InvoiceLeadDays = dto.InvoiceLeadDays; entity.InvoiceDueDays = dto.InvoiceDueDays;
        entity.QuotaWarningPercent = dto.QuotaWarningPercent; entity.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken); return Map(entity);
    }

    public async Task<SaasAutomationRunDto> RunAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow; var settings = await GetSettingsAsync(cancellationToken);
        var intervalMinutes = Math.Clamp(settings.RunIntervalMinutes, 5, 1440);
        var intervalBucket = (long)(now - DateTime.UnixEpoch).TotalMinutes / intervalMinutes;
        var runKey = force ? $"manual:{now:yyyyMMddHHmmssfffffff}" : $"scheduled:{intervalMinutes}:{intervalBucket}";
        var existing = await context.SaasAutomationRuns.AsNoTracking().FirstOrDefaultAsync(x => x.RunKey == runKey, cancellationToken);
        if (existing != null) return MapRun(existing);
        var run = new SaasAutomationRun { RunKey = runKey, Status = SaasAutomationRunStatus.Running, StartedAt = now };
        context.SaasAutomationRuns.Add(run);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            context.Entry(run).State = EntityState.Detached;
            return MapRun((await context.SaasAutomationRuns.AsNoTracking().SingleAsync(x => x.RunKey == runKey, cancellationToken)));
        }
        if (!settings.IsEnabled && !force) { run.Status = SaasAutomationRunStatus.Skipped; run.CompletedAt = DateTime.UtcNow; await context.SaveChangesAsync(cancellationToken); return MapRun(run); }

        try
        {
            await billingService.RefreshOverdueInvoicesAsync(cancellationToken);
            var subscriptions = await context.TenantSubscriptions.Include(x => x.Tenant).Include(x => x.Plan)
                .Where(x => x.Status != SubscriptionStatus.Cancelled).ToListAsync(cancellationToken);
            var createdNotifications = new List<SaasNotification>();
            var invoiceCandidates = new List<TenantSubscription>();
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var subscription in subscriptions)
            {
                run.ProcessedSubscriptions++;
                var isSuspended = subscription.Status == SubscriptionStatus.Suspended;
                if (!isSuspended && subscription.EndAt <= now)
                {
                    subscription.Status = SubscriptionStatus.Expired;
                    subscription.GracePeriodEndAt ??= subscription.EndAt.AddDays(settings.GracePeriodDays);
                    await AddNotificationAsync(createdNotifications, subscription, null, SaasNotificationType.SubscriptionExpired, SaasNotificationSeverity.Critical,
                        "اشتراک منقضی شد", $"اشتراک {subscription.Tenant.Name} در {subscription.EndAt:yyyy/MM/dd} منقضی شده است.", $"expired:{subscription.Id}:{subscription.EndAt:yyyyMMdd}", cancellationToken);
                    if (subscription.GracePeriodEndAt >= now && subscription.GracePeriodEndAt <= now.AddDays(settings.GraceWarningDays))
                        await AddNotificationAsync(createdNotifications, subscription, null, SaasNotificationType.GracePeriodEnding, SaasNotificationSeverity.Critical,
                            "پایان مهلت تنفس", $"مهلت فقط‌خواندنی {subscription.Tenant.Name} تا {subscription.GracePeriodEndAt:yyyy/MM/dd} پایان می‌یابد.", $"grace:{subscription.Id}:{subscription.GracePeriodEndAt:yyyyMMdd}", cancellationToken);
                }
                else if (!isSuspended && subscription.EndAt <= now.AddDays(settings.ExpiryWarningDays))
                {
                    subscription.Status = SubscriptionStatus.ExpiringSoon;
                    await AddNotificationAsync(createdNotifications, subscription, null, SaasNotificationType.SubscriptionExpiring, SaasNotificationSeverity.Warning,
                        "اشتراک نزدیک انقضا", $"اشتراک {subscription.Tenant.Name} تا {subscription.EndAt:yyyy/MM/dd} اعتبار دارد.", $"expiring:{subscription.Id}:{subscription.EndAt:yyyyMMdd}", cancellationToken);
                }
                else if (!isSuspended) subscription.Status = subscription.TrialEndAt > now ? SubscriptionStatus.Trial : SubscriptionStatus.Active;

                if (!isSuspended && settings.AutoCreateInvoices && subscription.AutoRenew && subscription.EndAt <= now.AddDays(settings.InvoiceLeadDays)) invoiceCandidates.Add(subscription);
                var users = await context.Users.IgnoreQueryFilters().CountAsync(x => x.TenantId == subscription.TenantId && !x.IsPlatformUser && x.IsActive, cancellationToken);
                var branches = await context.Branches.IgnoreQueryFilters().CountAsync(x => x.TenantId == subscription.TenantId && !x.IsArchived, cancellationToken);
                var transactions = await context.Transactions.IgnoreQueryFilters().CountAsync(x => x.TenantId == subscription.TenantId && x.CreatedAt >= monthStart, cancellationToken);
                var storage = await context.Documents.IgnoreQueryFilters().Where(x => x.TenantId == subscription.TenantId).SumAsync(x => (long?)x.FileSizeBytes, cancellationToken) ?? 0;
                var snapshot = await context.TenantUsageSnapshots.SingleOrDefaultAsync(x => x.TenantId == subscription.TenantId && x.PeriodStart == monthStart, cancellationToken);
                snapshot ??= new TenantUsageSnapshot { TenantId = subscription.TenantId, PeriodStart = monthStart };
                if (snapshot.Id == 0) context.TenantUsageSnapshots.Add(snapshot);
                snapshot.UserCount = users; snapshot.BranchCount = branches; snapshot.TransactionCount = transactions; snapshot.StorageBytes = storage; snapshot.CalculatedAt = now;
                await AddQuotaWarningAsync(createdNotifications, subscription, "کاربران", users, subscription.Plan.MaxUsers, settings.QuotaWarningPercent, monthStart, cancellationToken);
                await AddQuotaWarningAsync(createdNotifications, subscription, "شعبه‌ها", branches, subscription.Plan.MaxBranches, settings.QuotaWarningPercent, monthStart, cancellationToken);
                await AddQuotaWarningAsync(createdNotifications, subscription, "تراکنش‌ها", transactions, subscription.Plan.MaxMonthlyTransactions, settings.QuotaWarningPercent, monthStart, cancellationToken);
                await AddQuotaWarningAsync(createdNotifications, subscription, "فضای اسناد", storage, subscription.Plan.MaxStorageBytes, settings.QuotaWarningPercent, monthStart, cancellationToken);
                subscription.UpdatedAt = now;
            }
            await context.SaveChangesAsync(cancellationToken);

            foreach (var subscription in invoiceCandidates)
            {
                var periodStart = subscription.EndAt; var periodEnd = periodStart.AddMonths((int)subscription.BillingCycle);
                var exists = await context.SubscriptionInvoices.AnyAsync(x => x.SubscriptionId == subscription.Id && x.ServicePeriodStart == periodStart && x.Status != SubscriptionInvoiceStatus.Cancelled, cancellationToken);
                if (exists) continue;
                var invoice = await billingService.CreateInvoiceAsync(new CreateSubscriptionInvoiceDto
                {
                    SubscriptionId = subscription.Id, IssuedAt = now, DueAt = now.AddDays(settings.InvoiceDueDays), ServicePeriodStart = periodStart,
                    ServicePeriodEnd = periodEnd, Description = $"حق اشتراک {subscription.Plan.Name}", UnitPrice = subscription.AgreedPrice,
                    AutoRenewOnPayment = true, Notes = "فاکتور خودکار دوره اشتراک"
                }, cancellationToken);
                run.CreatedInvoices++;
                await AddNotificationAsync(createdNotifications, subscription, invoice.Id, SaasNotificationType.InvoiceCreated, SaasNotificationSeverity.Info,
                    "فاکتور اشتراک صادر شد", $"فاکتور {invoice.InvoiceNumber} به مبلغ {invoice.TotalAmount:N2} {invoice.CurrencyCode} صادر شد.", $"invoice:{invoice.Id}", cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
            var overdue = await context.SubscriptionInvoices.Include(x => x.Subscription).ThenInclude(x => x.Tenant).Where(x => x.Status == SubscriptionInvoiceStatus.Overdue).ToListAsync(cancellationToken);
            foreach (var invoice in overdue)
                await AddNotificationAsync(createdNotifications, invoice.Subscription, invoice.Id, SaasNotificationType.PaymentOverdue, SaasNotificationSeverity.Critical,
                    "پرداخت معوق", $"فاکتور {invoice.InvoiceNumber} صرافی {invoice.Subscription.Tenant.Name} سررسید شده است.", $"overdue:{invoice.Id}:{invoice.DueAt:yyyyMMdd}", cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            run.CreatedNotifications = createdNotifications.Count;
            if (settings.SendEmailNotifications)
            {
                foreach (var notification in createdNotifications) await DeliverAsync(notification, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                foreach (var notification in createdNotifications) notification.DeliveryStatus = SaasNotificationDeliveryStatus.Skipped;
            }
            settings.LastRunAt = DateTime.UtcNow; run.Status = SaasAutomationRunStatus.Completed; run.CompletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken); return MapRun(run);
        }
        catch (Exception ex)
        {
            run.Status = SaasAutomationRunStatus.Failed; run.Error = ex.ToString()[..Math.Min(4000, ex.ToString().Length)]; run.CompletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken); throw;
        }
    }

    public async Task MarkReadAsync(long notificationId, CancellationToken cancellationToken = default)
    { var item = await context.SaasNotifications.FindAsync([notificationId], cancellationToken) ?? throw new KeyNotFoundException(); item.ReadAt ??= DateTime.UtcNow; await context.SaveChangesAsync(cancellationToken); }

    private async Task AddQuotaWarningAsync(List<SaasNotification> list, TenantSubscription sub, string label, long used, long limit, int threshold, DateTime period, CancellationToken ct)
    { if (limit <= 0 || used * 100 < limit * threshold) return; await AddNotificationAsync(list, sub, null, SaasNotificationType.QuotaWarning, used >= limit ? SaasNotificationSeverity.Critical : SaasNotificationSeverity.Warning, $"هشدار سقف {label}", $"مصرف {label} در {sub.Tenant.Name}: {used:N0} از {limit:N0}.", $"quota:{sub.TenantId}:{label}:{period:yyyyMM}:{threshold}", ct); }

    private async Task AddNotificationAsync(List<SaasNotification> list, TenantSubscription sub, long? invoiceId, SaasNotificationType type, SaasNotificationSeverity severity, string title, string message, string key, CancellationToken ct)
    {
        if (list.Any(x => x.DeduplicationKey == key) || await context.SaasNotifications.AnyAsync(x => x.DeduplicationKey == key, ct)) return;
        var item = new SaasNotification { TenantId = sub.TenantId, SubscriptionId = sub.Id, InvoiceId = invoiceId, Type = type, Severity = severity, Title = title, Message = message, DeduplicationKey = key, RecipientEmail = sub.Tenant.ContactEmail, DeliveryStatus = SaasNotificationDeliveryStatus.Pending };
        context.SaasNotifications.Add(item); list.Add(item);
    }
    private async Task DeliverAsync(SaasNotification item, CancellationToken ct)
    {
        if (!messageSender.IsConfigured || string.IsNullOrWhiteSpace(item.RecipientEmail)) { item.DeliveryStatus = SaasNotificationDeliveryStatus.Skipped; return; }
        try { await messageSender.SendEmailAsync(item.RecipientEmail, item.Title, $"<div dir='rtl' style='font-family:Tahoma;line-height:1.9'><h2>{item.Title}</h2><p>{item.Message}</p></div>", ct); item.DeliveryStatus = SaasNotificationDeliveryStatus.Sent; item.SentAt = DateTime.UtcNow; }
        catch (Exception ex) { item.DeliveryStatus = SaasNotificationDeliveryStatus.Failed; item.DeliveryError = ex.Message[..Math.Min(2000, ex.Message.Length)]; }
    }
    private async Task<SaasAutomationSettings> GetSettingsAsync(CancellationToken ct)
    {
        var entity = await context.SaasAutomationSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (entity != null) return entity;
        await context.Database.ExecuteSqlRawAsync("""
            SET IDENTITY_INSERT [SaasAutomationSettings] ON;
            IF NOT EXISTS (SELECT 1 FROM [SaasAutomationSettings] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 1)
                INSERT INTO [SaasAutomationSettings]
                    ([Id],[IsEnabled],[AutoCreateInvoices],[SendEmailNotifications],[RunIntervalMinutes],[ExpiryWarningDays],[GracePeriodDays],[GraceWarningDays],[InvoiceLeadDays],[InvoiceDueDays],[QuotaWarningPercent])
                VALUES (1,1,1,0,360,30,7,2,7,7,80);
            SET IDENTITY_INSERT [SaasAutomationSettings] OFF;
            """, ct);
        return await context.SaasAutomationSettings.SingleAsync(x => x.Id == 1, ct);
    }
    private static SaasAutomationSettingsDto Map(SaasAutomationSettings x) => new() { IsEnabled=x.IsEnabled, AutoCreateInvoices=x.AutoCreateInvoices, SendEmailNotifications=x.SendEmailNotifications, RunIntervalMinutes=x.RunIntervalMinutes, ExpiryWarningDays=x.ExpiryWarningDays, GracePeriodDays=x.GracePeriodDays, GraceWarningDays=x.GraceWarningDays, InvoiceLeadDays=x.InvoiceLeadDays, InvoiceDueDays=x.InvoiceDueDays, QuotaWarningPercent=x.QuotaWarningPercent, LastRunAt=x.LastRunAt };
    private static SaasAutomationRunDto MapRun(SaasAutomationRun x) => new() { Id=x.Id, RunKey=x.RunKey, Status=x.Status, StartedAt=x.StartedAt, CompletedAt=x.CompletedAt, ProcessedSubscriptions=x.ProcessedSubscriptions, CreatedInvoices=x.CreatedInvoices, CreatedNotifications=x.CreatedNotifications, Error=x.Error };
}
