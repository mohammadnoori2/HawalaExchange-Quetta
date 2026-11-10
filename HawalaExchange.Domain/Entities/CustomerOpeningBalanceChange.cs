using System.ComponentModel.DataAnnotations;

namespace HawalaExchange.Domain.Entities;

public sealed class CustomerOpeningBalanceChange : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long OriginalLedgerEntryId { get; set; }
    public long ReversalTransactionId { get; set; }
    public long? ReplacementTransactionId { get; set; }
    public long ChangedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? Reason { get; set; }
}
