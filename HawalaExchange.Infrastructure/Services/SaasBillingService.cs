using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class SaasBillingService(ApplicationDbContext context) : ISaasBillingService
{
    private static readonly SubscriptionInvoiceStatus[] OpenStatuses =
        [SubscriptionInvoiceStatus.Issued, SubscriptionInvoiceStatus.PartiallyPaid, SubscriptionInvoiceStatus.Overdue];

    public async Task<BillingDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await RefreshOverdueInvoicesAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var subscriptions = await context.TenantSubscriptions.AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Trial || x.Status == SubscriptionStatus.ExpiringSoon)
            .Select(x => new { x.CurrencyCode, x.AgreedPrice, x.BillingCycle })
            .ToListAsync(cancellationToken);
        var invoices = await context.SubscriptionInvoices.AsNoTracking()
            .Select(x => new { x.CurrencyCode, x.Status, x.IssuedAt, x.DueAt, x.TotalAmount, x.PaidAmount })
            .ToListAsync(cancellationToken);
        var collected = await context.SubscriptionPayments.AsNoTracking()
            .Where(x => x.Status == SubscriptionPaymentStatus.Paid && x.PaidAt >= monthStart)
            .GroupBy(x => x.CurrencyCode)
            .Select(x => new { Currency = x.Key, Amount = x.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.Currency, x => x.Amount, cancellationToken);

        var currencies = subscriptions.Select(x => x.CurrencyCode)
            .Concat(invoices.Select(x => x.CurrencyCode)).Concat(collected.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var summaries = currencies.Select(currency =>
        {
            var mrr = subscriptions.Where(x => x.CurrencyCode.Equals(currency, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.AgreedPrice / Math.Max(1, (int)x.BillingCycle));
            var currencyInvoices = invoices.Where(x => x.CurrencyCode.Equals(currency, StringComparison.OrdinalIgnoreCase)).ToList();
            return new BillingCurrencySummaryDto
            {
                CurrencyCode = currency,
                Mrr = mrr,
                Arr = mrr * 12,
                BilledThisMonth = currencyInvoices.Where(x => x.IssuedAt >= monthStart && x.Status != SubscriptionInvoiceStatus.Cancelled).Sum(x => x.TotalAmount),
                CollectedThisMonth = collected.GetValueOrDefault(currency),
                Outstanding = currencyInvoices.Where(x => OpenStatuses.Contains(x.Status)).Sum(x => x.TotalAmount - x.PaidAmount),
                Overdue = currencyInvoices.Where(x => x.Status == SubscriptionInvoiceStatus.Overdue).Sum(x => x.TotalAmount - x.PaidAmount)
            };
        }).ToList();

        return new BillingDashboardDto
        {
            IssuedInvoices = invoices.Count(x => OpenStatuses.Contains(x.Status)),
            OverdueInvoices = invoices.Count(x => x.Status == SubscriptionInvoiceStatus.Overdue),
            Currencies = summaries,
            RecentInvoices = (await GetInvoicesAsync(cancellationToken: cancellationToken)).Take(8).ToList()
        };
    }

    public async Task<IReadOnlyList<BillingSubscriptionOptionDto>> GetSubscriptionOptionsAsync(CancellationToken cancellationToken = default) =>
        await context.TenantSubscriptions.AsNoTracking()
            .Where(x => x.Status != SubscriptionStatus.Cancelled)
            .OrderBy(x => x.Tenant.Name).ThenByDescending(x => x.StartAt)
            .Select(x => new BillingSubscriptionOptionDto
            {
                SubscriptionId = x.Id, TenantId = x.TenantId, TenantName = x.Tenant.Name,
                PlanName = x.Plan.Name, Price = x.AgreedPrice, CurrencyCode = x.CurrencyCode,
                PeriodStart = x.EndAt, PeriodEnd = x.EndAt.AddMonths((int)x.BillingCycle), AutoRenew = x.AutoRenew
            }).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionInvoiceDto>> GetInvoicesAsync(
        string? search = null, SubscriptionInvoiceStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = context.SubscriptionInvoices.AsNoTracking().Include(x => x.Tenant).Include(x => x.Subscription).ThenInclude(x => x.Plan).AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.InvoiceNumber.Contains(term) || x.Tenant.Name.Contains(term) || x.BillingName.Contains(term));
        }
        var rows = await query.OrderByDescending(x => x.IssuedAt).ThenByDescending(x => x.Id).ToListAsync(cancellationToken);
        return rows.Select(x => MapInvoice(x)).ToList();
    }

    public async Task<SubscriptionInvoiceDto?> GetInvoiceAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await context.SubscriptionInvoices.AsNoTracking()
            .Include(x => x.Tenant).Include(x => x.Subscription).ThenInclude(x => x.Plan)
            .Include(x => x.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : MapInvoice(entity, true);
    }

    public async Task<SubscriptionInvoiceDto> CreateInvoiceAsync(CreateSubscriptionInvoiceDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.DueAt < dto.IssuedAt) throw new InvalidOperationException("تاریخ سررسید نمی‌تواند قبل از تاریخ صدور باشد.");
        if (dto.ServicePeriodEnd <= dto.ServicePeriodStart) throw new InvalidOperationException("پایان دوره خدمت باید بعد از شروع آن باشد.");
        var subtotal = decimal.Round(dto.Quantity * dto.UnitPrice, 2);
        if (dto.DiscountAmount > subtotal) throw new InvalidOperationException("تخفیف نمی‌تواند بیشتر از مبلغ اولیه باشد.");
        var total = subtotal - dto.DiscountAmount + dto.TaxAmount;
        var subscription = await context.TenantSubscriptions.Include(x => x.Tenant).Include(x => x.Plan)
            .FirstOrDefaultAsync(x => x.Id == dto.SubscriptionId, cancellationToken)
            ?? throw new KeyNotFoundException("اشتراک مورد نظر یافت نشد.");

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var number = await NextInvoiceNumberAsync(dto.IssuedAt.Year, cancellationToken);
        var invoice = new SubscriptionInvoice
        {
            InvoiceNumber = number, TenantId = subscription.TenantId, SubscriptionId = subscription.Id,
            Status = SubscriptionInvoiceStatus.Issued, IssuedAt = ToUtc(dto.IssuedAt), DueAt = ToUtc(dto.DueAt),
            ServicePeriodStart = ToUtc(dto.ServicePeriodStart), ServicePeriodEnd = ToUtc(dto.ServicePeriodEnd),
            Subtotal = subtotal, DiscountAmount = dto.DiscountAmount, TaxAmount = dto.TaxAmount, TotalAmount = total,
            CurrencyCode = subscription.CurrencyCode, BillingName = subscription.Tenant.LegalName ?? subscription.Tenant.Name,
            BillingEmail = subscription.Tenant.ContactEmail, BillingPhone = subscription.Tenant.ContactPhone,
            Notes = dto.Notes?.Trim(), AutoRenewOnPayment = dto.AutoRenewOnPayment, CreatedAt = DateTime.UtcNow
        };
        invoice.Items.Add(new SubscriptionInvoiceItem
        {
            Description = dto.Description.Trim(), Quantity = dto.Quantity, UnitPrice = dto.UnitPrice,
            DiscountAmount = dto.DiscountAmount, TaxAmount = dto.TaxAmount, LineTotal = total
        });
        context.SubscriptionInvoices.Add(invoice);
        AddAudit("CREATE_INVOICE", nameof(SubscriptionInvoice), null, $"Invoice={number}; Total={total} {invoice.CurrencyCode}", invoice.TenantId);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetInvoiceAsync(invoice.Id, cancellationToken))!;
    }

    public async Task RecordPaymentAsync(RecordInvoicePaymentDto dto, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var invoice = await context.SubscriptionInvoices.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == dto.InvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException("فاکتور مورد نظر یافت نشد.");
        if (invoice.Status is SubscriptionInvoiceStatus.Cancelled or SubscriptionInvoiceStatus.Refunded)
            throw new InvalidOperationException("برای این فاکتور امکان ثبت پرداخت وجود ندارد.");
        var balance = invoice.TotalAmount - invoice.PaidAmount;
        if (dto.Amount > balance) throw new InvalidOperationException($"مبلغ پرداخت از باقی‌مانده فاکتور ({balance:N2}) بیشتر است.");
        var paidAt = ToUtc(dto.PaidAt);
        context.SubscriptionPayments.Add(new SubscriptionPayment
        {
            SubscriptionId = invoice.SubscriptionId, InvoiceId = invoice.Id, Amount = dto.Amount,
            CurrencyCode = invoice.CurrencyCode, Status = SubscriptionPaymentStatus.Paid, DueAt = invoice.DueAt,
            PaidAt = paidAt, PaymentMethod = dto.PaymentMethod?.Trim(), ReferenceNumber = dto.ReferenceNumber?.Trim(),
            ProviderName = dto.ProviderName?.Trim(), ProviderTransactionId = dto.ProviderTransactionId?.Trim(),
            ReceiptNumber = dto.ReceiptNumber?.Trim(), Note = dto.Note?.Trim(), CreatedAt = DateTime.UtcNow
        });
        invoice.PaidAmount += dto.Amount;
        invoice.UpdatedAt = DateTime.UtcNow;
        invoice.Status = invoice.PaidAmount >= invoice.TotalAmount ? SubscriptionInvoiceStatus.Paid : SubscriptionInvoiceStatus.PartiallyPaid;
        if (invoice.Status == SubscriptionInvoiceStatus.Paid)
        {
            invoice.PaidAt = paidAt;
            invoice.Subscription.LastPaymentAt = paidAt;
            if (invoice.AutoRenewOnPayment && invoice.RenewalAppliedAt is null)
            {
                var periodEnd = invoice.ServicePeriodEnd;
                if (periodEnd > invoice.Subscription.EndAt) invoice.Subscription.EndAt = periodEnd;
                invoice.Subscription.Status = SubscriptionStatus.Active;
                invoice.Subscription.GracePeriodEndAt = null;
                invoice.Subscription.NextPaymentAt = periodEnd;
                invoice.Subscription.UpdatedAt = DateTime.UtcNow;
                invoice.RenewalAppliedAt = DateTime.UtcNow;
            }
        }
        AddAudit("RECORD_INVOICE_PAYMENT", nameof(SubscriptionInvoice), invoice.Id, $"Amount={dto.Amount} {invoice.CurrencyCode}", invoice.TenantId);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelInvoiceAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await context.SubscriptionInvoices.FindAsync([invoiceId], cancellationToken)
            ?? throw new KeyNotFoundException("فاکتور مورد نظر یافت نشد.");
        if (invoice.PaidAmount > 0) throw new InvalidOperationException("فاکتور دارای پرداخت را نمی‌توان لغو کرد.");
        invoice.Status = SubscriptionInvoiceStatus.Cancelled;
        invoice.UpdatedAt = DateTime.UtcNow;
        AddAudit("CANCEL_INVOICE", nameof(SubscriptionInvoice), invoice.Id, invoice.InvoiceNumber, invoice.TenantId);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshOverdueInvoicesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var rows = await context.SubscriptionInvoices.Where(x =>
            (x.Status == SubscriptionInvoiceStatus.Issued || x.Status == SubscriptionInvoiceStatus.PartiallyPaid) && x.DueAt < now).ToListAsync(cancellationToken);
        foreach (var row in rows) { row.Status = SubscriptionInvoiceStatus.Overdue; row.UpdatedAt = now; }
        if (rows.Count > 0) await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> NextInvoiceNumberAsync(int year, CancellationToken cancellationToken)
    {
        var sequence = await context.BillingNumberSequences.SingleOrDefaultAsync(x => x.Year == year && x.Prefix == "INV", cancellationToken);
        if (sequence is null)
        {
            sequence = new BillingNumberSequence { Year = year, Prefix = "INV", NextValue = 2 };
            context.BillingNumberSequences.Add(sequence);
            return $"INV-{year}-000001";
        }
        var value = sequence.NextValue++;
        return $"INV-{year}-{value:000000}";
    }

    private void AddAudit(string action, string entityName, long? entityId, string? details, long? tenantId) =>
        context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            ActorUserId = context.CurrentUserId > 0 ? context.CurrentUserId : null, TenantId = tenantId,
            Action = action, EntityName = entityName, EntityId = entityId, Details = details, CreatedAt = DateTime.UtcNow
        });

    private static SubscriptionInvoiceDto MapInvoice(SubscriptionInvoice x, bool includeItems = false) => new()
    {
        Id = x.Id, InvoiceNumber = x.InvoiceNumber, TenantId = x.TenantId, TenantName = x.Tenant.Name,
        SubscriptionId = x.SubscriptionId, PlanName = x.Subscription.Plan.Name, Status = x.Status,
        IssuedAt = x.IssuedAt, DueAt = x.DueAt, ServicePeriodStart = x.ServicePeriodStart, ServicePeriodEnd = x.ServicePeriodEnd,
        PaidAt = x.PaidAt, Subtotal = x.Subtotal, DiscountAmount = x.DiscountAmount, TaxAmount = x.TaxAmount,
        TotalAmount = x.TotalAmount, PaidAmount = x.PaidAmount, CurrencyCode = x.CurrencyCode,
        BillingName = x.BillingName, BillingEmail = x.BillingEmail, BillingPhone = x.BillingPhone,
        Notes = x.Notes, AutoRenewOnPayment = x.AutoRenewOnPayment,
        Items = includeItems ? x.Items.Select(i => new SubscriptionInvoiceItemDto
        {
            Id = i.Id, Description = i.Description, Quantity = i.Quantity, UnitPrice = i.UnitPrice,
            DiscountAmount = i.DiscountAmount, TaxAmount = i.TaxAmount, LineTotal = i.LineTotal
        }).ToList() : []
    };

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
