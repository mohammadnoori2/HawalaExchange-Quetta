using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("TenantUsageSnapshots")]
public sealed class TenantUsageSnapshot
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public long TenantId { get; set; }
    [Column(TypeName = "date")]
    public DateTime PeriodStart { get; set; }
    public int UserCount { get; set; }
    public int BranchCount { get; set; }
    public int TransactionCount { get; set; }
    public long StorageBytes { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    public Tenant Tenant { get; set; } = null!;
}
