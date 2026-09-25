using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("PaymentLocationCorrespondentAssignments")]
public sealed class PaymentLocationCorrespondentAssignment : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long PaymentLocationId { get; set; }
    public long CorrespondentId { get; set; }
    [Column(TypeName = "date")]
    public DateTime EffectiveFrom { get; set; }
    [Column(TypeName = "date")]
    public DateTime? EffectiveTo { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PaymentLocation PaymentLocation { get; set; } = null!;
    public Correspondent Correspondent { get; set; } = null!;
}
