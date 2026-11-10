namespace HawalaExchange.Application.DTOs;

public sealed class HawalaCancellationPreviewDto
{
    public long HawalaId { get; set; }
    public long Number { get; set; }
    public string Sender { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string PaymentLocation { get; set; } = "";
    public bool HasCommission { get; set; }
    public bool HasConversion { get; set; }
    public bool IsClosedPeriod { get; set; }
    public bool IsCancelled { get; set; }
    public bool RequiresCompensation => HasCommission && HasConversion;
    public string AccountingMode { get; set; } = "";
    public long? CompensationHawalaId { get; set; }
    public decimal? UsdAmount { get; set; }
    public string ConfirmationToken { get; set; } = "";
    public List<HawalaCancellationEntryDto> Entries { get; set; } = [];
    public List<HawalaCancellationRateDto> Rates { get; set; } = [];
}

public sealed class HawalaCancellationEntryDto
{
    public long AccountId { get; set; }
    public string Account { get; set; } = "";
    public long CurrencyId { get; set; }
    public string Currency { get; set; } = "";
    public decimal BadehKar { get; set; }
    public decimal TalabKar { get; set; }
}

public sealed class HawalaCancellationRateDto
{
    public string Correspondent { get; set; } = "";
    public string SourceCurrency { get; set; } = "";
    public string TargetCurrency { get; set; } = "";
    public decimal SourceAmount { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal Rate { get; set; }
}

public sealed class HawalaCompensationRequestDto
{
    public string ConfirmationToken { get; set; } = "";
    public bool Manual { get; set; }
    public long? Number { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ReceiverName { get; set; }
    public string? Notes { get; set; }
}
