using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

[Table("CashDailyBalances")]
public class CashDailyBalance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public DateTime JournalDate { get; set; }

    public long AccountId { get; set; }

    public long CurrencyId { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal? ClosingBalance { get; set; }

    public bool IsClosed { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ModifiedAt { get; set; }

    [ForeignKey(nameof(AccountId))]
    public Account Account { get; set; } = null!;

    [ForeignKey(nameof(CurrencyId))]
    public Currency Currency { get; set; } = null!;
}
