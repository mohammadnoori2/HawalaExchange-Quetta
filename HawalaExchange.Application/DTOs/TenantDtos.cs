using System.ComponentModel.DataAnnotations;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.DTOs;

public sealed class TenantDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int UserCount { get; set; }
    public int BranchCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreateTenantDto
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)] public string? LegalName { get; set; }
    [StringLength(200)] public string? ContactName { get; set; }
    [EmailAddress, StringLength(256)] public string? ContactEmail { get; set; }
    [StringLength(50)] public string? ContactPhone { get; set; }

    [Required, StringLength(100)]
    public string AdminUserName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string AdminFullName { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 6)]
    public string AdminPassword { get; set; } = string.Empty;

    [Range(1, long.MaxValue)] public long PlanId { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateTime SubscriptionStartAt { get; set; } = DateTime.UtcNow.Date;
    public DateTime SubscriptionEndAt { get; set; } = DateTime.UtcNow.Date.AddMonths(1);
    public bool AutoRenew { get; set; }
    [Range(0, double.MaxValue)] public decimal AgreedPrice { get; set; }
    [Required, StringLength(10)] public string SubscriptionCurrencyCode { get; set; } = "USD";
}

public sealed class UpdateTenantDto
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
