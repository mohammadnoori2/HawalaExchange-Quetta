using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.DTOs;

public sealed class SaasReportFilterDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public long? PlanId { get; set; }
    public SubscriptionStatus? Status { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Search { get; set; }
    public string SortBy { get; set; } = "name";
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
public sealed class SaasGrowthPointDto { public DateTime Month { get; set; } public int NewTenants { get; set; } public int NewUsers { get; set; } public int NewSubscriptions { get; set; } }
public sealed class SaasRevenuePointDto { public DateTime Month { get; set; } public string CurrencyCode { get; set; } = string.Empty; public decimal Billed { get; set; } public decimal Collected { get; set; } public decimal Outstanding { get; set; } }
public sealed class SaasTenantReportRowDto
{
    public long TenantId { get; set; } public string TenantName { get; set; } = string.Empty; public string PlanName { get; set; } = string.Empty;
    public bool TenantIsActive { get; set; }
    public long? PlanId { get; set; }
    public SubscriptionStatus? Status { get; set; } public DateTime CreatedAt { get; set; } public DateTime? EndAt { get; set; }
    public int Users { get; set; } public int Branches { get; set; } public int Transactions { get; set; } public long StorageBytes { get; set; }
    public decimal SubscriptionPrice { get; set; } public string CurrencyCode { get; set; } = string.Empty; public decimal Outstanding { get; set; }
}
public sealed class SaasOverdueReportRowDto { public string InvoiceNumber { get; set; } = string.Empty; public string TenantName { get; set; } = string.Empty; public DateTime DueAt { get; set; } public decimal Total { get; set; } public decimal Paid { get; set; } public decimal Balance { get; set; } public string CurrencyCode { get; set; } = string.Empty; }
public sealed class SaasManagementReportDto
{
    public DateTime GeneratedAt { get; set; } public int TotalTenants { get; set; } public int ActiveTenants { get; set; } public int InactiveTenants { get; set; } public int ExpiredTenants { get; set; }
    public int SuspendedTenants { get; set; } public int TotalUsers { get; set; } public int TotalRows { get; set; }
    public IReadOnlyList<SaasGrowthPointDto> Growth { get; set; } = []; public IReadOnlyList<SaasRevenuePointDto> Revenue { get; set; } = [];
    public IReadOnlyList<SaasTenantReportRowDto> Tenants { get; set; } = []; public IReadOnlyList<SaasOverdueReportRowDto> OverdueInvoices { get; set; } = [];
}
public sealed class SaasReportExportDto { public byte[] Content { get; set; } = []; public string FileName { get; set; } = string.Empty; public string ContentType { get; set; } = string.Empty; }
