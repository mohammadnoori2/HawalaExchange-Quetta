using System.ComponentModel.DataAnnotations;

namespace HawalaExchange.Domain.Entities;

/// <summary>Durable confirmation request. The import itself remains one atomic transaction.</summary>
public sealed class HawalaImportJob : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long BatchId { get; set; }
    public long RequestedBy { get; set; }
    [Required] public string RequestJson { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Status { get; set; } = "Queued";
    public int ProgressPercent { get; set; }
    [MaxLength(500)] public string ProgressMessage { get; set; } = string.Empty;
    [MaxLength(2000)] public string? ErrorMessage { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public HawalaImportBatch Batch { get; set; } = null!;
}
