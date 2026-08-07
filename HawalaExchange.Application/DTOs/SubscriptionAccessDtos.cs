using System.ComponentModel.DataAnnotations;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.DTOs;

public sealed class SubscriptionAccessDto
{
    public long TenantId { get; set; }
    public long? SubscriptionId { get; set; }
    public SubscriptionAccessLevel AccessLevel { get; set; }
    public SubscriptionStatus? Status { get; set; }
    public string? PlanName { get; set; }
    public DateTime? EndAt { get; set; }
    public DateTime? GracePeriodEndAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool CanSignIn => AccessLevel != SubscriptionAccessLevel.Blocked;
    public bool CanWrite => AccessLevel == SubscriptionAccessLevel.Full;
}

public sealed class PlatformUserDto
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public sealed class CreatePlatformUserDto
{
    [Required, StringLength(100)] public string UserName { get; set; } = string.Empty;
    [Required, StringLength(200)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = PlatformRoles.Support;
}
