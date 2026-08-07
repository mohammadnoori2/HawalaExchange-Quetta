using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("CompanySettings")]
public class CompanySetting : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long TenantId { get; set; }

    [ForeignKey(nameof(TenantId))]
    public Tenant Tenant { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? LogoPath { get; set; }

    [MaxLength(50)]
    public string? PhoneNumber { get; set; }

    [MaxLength(50)]
    public string? WhatsAppNumber { get; set; }

    [MaxLength(100)]
    public string? TelegramUserName { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(1000)]
    public string? FooterNote { get; set; }

    /// <summary>
    /// ارز اصلی که بهای تمام‌شده، مفاد و ضرر تبدیل پول بر اساس آن محاسبه می‌شود.
    /// </summary>
    public long? DefaultProfitCurrencyId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public Currency? DefaultProfitCurrency { get; set; }
}
