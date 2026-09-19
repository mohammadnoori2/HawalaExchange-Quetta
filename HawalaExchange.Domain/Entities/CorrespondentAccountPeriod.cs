using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities;

public sealed class CorrespondentAccountPeriod : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long CorrespondentId { get; set; }
    public int PeriodNumber { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public long ClosedBy { get; set; }
    public DateTime ClosedAt { get; set; }
    public Correspondent Correspondent { get; set; } = null!;
    public ICollection<CorrespondentAccountPeriodBalance> Balances { get; set; } = [];
    public ICollection<CorrespondentAccountPeriodHawala> Hawalas { get; set; } = [];
}

public sealed class CorrespondentAccountPeriodBalance : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long PeriodId { get; set; }
    public long CurrencyId { get; set; }
    [Column(TypeName="decimal(18,4)")] public decimal TalabKar { get; set; }
    [Column(TypeName="decimal(18,4)")] public decimal BadehKar { get; set; }
    public CorrespondentAccountPeriod Period { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
}

public sealed class CorrespondentAccountPeriodHawala : ITenantEntity
{
    public long TenantId { get; set; }
    public long PeriodId { get; set; }
    public long HawalaId { get; set; }
    public CorrespondentAccountPeriod Period { get; set; } = null!;
    public Hawala Hawala { get; set; } = null!;
}
