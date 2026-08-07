using System.ComponentModel.DataAnnotations;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.DTOs;

public sealed class BillingCurrencySummaryDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Mrr { get; set; }
    public decimal Arr { get; set; }
    public decimal BilledThisMonth { get; set; }
    public decimal CollectedThisMonth { get; set; }
    public decimal Outstanding { get; set; }
    public decimal Overdue { get; set; }
}

public sealed class BillingDashboardDto
{
    public int IssuedInvoices { get; set; }
    public int OverdueInvoices { get; set; }
    public IReadOnlyList<BillingCurrencySummaryDto> Currencies { get; set; } = [];
    public IReadOnlyList<SubscriptionInvoiceDto> RecentInvoices { get; set; } = [];
}

public sealed class SubscriptionInvoiceItemDto
{
    public long Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class SubscriptionInvoiceDto
{
    public long Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public long TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public long SubscriptionId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public SubscriptionInvoiceStatus Status { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime ServicePeriodStart { get; set; }
    public DateTime ServicePeriodEnd { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Balance => TotalAmount - PaidAmount;
    public string CurrencyCode { get; set; } = string.Empty;
    public string BillingName { get; set; } = string.Empty;
    public string? BillingEmail { get; set; }
    public string? BillingPhone { get; set; }
    public string? Notes { get; set; }
    public bool AutoRenewOnPayment { get; set; }
    public IReadOnlyList<SubscriptionInvoiceItemDto> Items { get; set; } = [];
}

public sealed class BillingSubscriptionOptionDto
{
    public long SubscriptionId { get; set; }
    public long TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public bool AutoRenew { get; set; }
}

public sealed class CreateSubscriptionInvoiceDto
{
    [Range(1, long.MaxValue)] public long SubscriptionId { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime ServicePeriodStart { get; set; } = DateTime.UtcNow;
    public DateTime ServicePeriodEnd { get; set; } = DateTime.UtcNow.AddMonths(1);
    [Required, StringLength(500)] public string Description { get; set; } = "حق اشتراک نرم‌افزار";
    [Range(0.0001, double.MaxValue)] public decimal Quantity { get; set; } = 1;
    [Range(0, double.MaxValue)] public decimal UnitPrice { get; set; }
    [Range(0, double.MaxValue)] public decimal DiscountAmount { get; set; }
    [Range(0, double.MaxValue)] public decimal TaxAmount { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public bool AutoRenewOnPayment { get; set; }
}

public sealed class RecordInvoicePaymentDto
{
    [Range(1, long.MaxValue)] public long InvoiceId { get; set; }
    [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    [StringLength(100)] public string? PaymentMethod { get; set; }
    [StringLength(150)] public string? ReferenceNumber { get; set; }
    [StringLength(100)] public string? ProviderName { get; set; }
    [StringLength(200)] public string? ProviderTransactionId { get; set; }
    [StringLength(100)] public string? ReceiptNumber { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
}
