namespace HawalaExchange.Application.DTOs;

public sealed class PlatformAuditFilterDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public long? TenantId { get; set; }
    public string? Action { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class PlatformAuditRowDto
{
    public long Id { get; set; }
    public long? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public long? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PlatformAuditPageDto
{
    public int TotalRows { get; set; }
    public IReadOnlyList<PlatformAuditRowDto> Rows { get; set; } = [];
}
