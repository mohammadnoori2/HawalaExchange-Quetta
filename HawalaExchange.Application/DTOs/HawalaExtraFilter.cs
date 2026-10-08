namespace HawalaExchange.Application.DTOs;
public sealed class HawalaExtraFilter
{
    public int Count => new[]
    {
        !string.IsNullOrWhiteSpace(Sender), !string.IsNullOrWhiteSpace(Receiver), !string.IsNullOrWhiteSpace(Reference),
        !string.IsNullOrWhiteSpace(Registration), !string.IsNullOrWhiteSpace(Commission), MinNumber.HasValue, MaxNumber.HasValue,
        CorrespondentId.HasValue, PaymentLocationId.HasValue, CurrencyId.HasValue, MinAmount.HasValue, MaxAmount.HasValue,
        From != DateTime.MinValue, To != DateTime.MinValue
    }.Count(x => x);
    public HawalaExtraFilter Copy() => (HawalaExtraFilter)MemberwiseClone();
    public string Sender { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Registration { get; set; } = "";
    public string Commission { get; set; } = "";
    public long? MinNumber { get; set; }
    public long? MaxNumber { get; set; }
    public long? CorrespondentId { get; set; }
    public long? PaymentLocationId { get; set; }
    public long? CurrencyId { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public DateTime From { get; set; } = DateTime.MinValue;
    public DateTime To { get; set; } = DateTime.MinValue;
}
