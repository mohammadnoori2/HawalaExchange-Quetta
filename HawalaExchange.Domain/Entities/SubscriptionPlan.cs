using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("SubscriptionPlans")]
public sealed class SubscriptionPlan
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MonthlyPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AnnualPrice { get; set; }

    [Required, MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    public int TrialDays { get; set; }
    public int MaxUsers { get; set; }
    public int MaxBranches { get; set; }
    public long MaxStorageBytes { get; set; }
    public int MaxMonthlyTransactions { get; set; }
    public bool IncludesAdvancedReports { get; set; }
    public bool IncludesDocumentManagement { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<TenantSubscription> Subscriptions { get; set; } = new List<TenantSubscription>();
}
