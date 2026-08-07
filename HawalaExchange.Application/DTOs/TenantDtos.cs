using System.ComponentModel.DataAnnotations;

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

    [Required, StringLength(100)]
    public string AdminUserName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string AdminFullName { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 6)]
    public string AdminPassword { get; set; } = string.Empty;
}

public sealed class UpdateTenantDto
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
