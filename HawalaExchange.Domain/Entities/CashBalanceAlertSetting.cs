using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("CashBalanceAlertSettings")]
public sealed class CashBalanceAlertSetting : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long TenantId { get; set; }
    public long AccountId { get; set; }
    public long CurrencyId { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal MinimumBalance { get; set; }

    public bool IsActive { get; set; } = true;
    public bool NotifyAllUsers { get; set; } = true;
    public bool ShowInApp { get; set; } = true;
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Account Account { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<CashBalanceAlertRecipient> Recipients { get; set; } = [];
    public ICollection<CashBalanceAlert> Alerts { get; set; } = [];
}

[Table("CashBalanceAlertRecipients")]
public sealed class CashBalanceAlertRecipient : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long TenantId { get; set; }
    public long SettingId { get; set; }
    public long UserId { get; set; }

    public CashBalanceAlertSetting Setting { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}

[Table("CashBalanceAlerts")]
public sealed class CashBalanceAlert : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long TenantId { get; set; }
    public long SettingId { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal CurrentBalance { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal MinimumBalance { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    public CashBalanceAlertSetting Setting { get; set; } = null!;
}
