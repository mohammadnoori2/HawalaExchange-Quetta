using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("DailyCommissionRates")]
public sealed class DailyCommissionRate : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long TenantId { get; set; }
    public DateTime RateDate { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal UsdToAfnRate { get; set; }

    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
