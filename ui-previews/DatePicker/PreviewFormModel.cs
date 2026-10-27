using System.ComponentModel.DataAnnotations;

namespace HawalaExchange.DatePickerPreview;

public sealed class PreviewFormModel : IValidatableObject
{
    public string Section { get; set; } = "accounts";
    [Required(ErrorMessage = "این نام را وارد کنید.")]
    public string Name { get; set; } = "";
    public string OtherName { get; set; } = "";
    public string Code { get; set; } = "";
    public string Phone { get; set; } = "";
    public string IdentityNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Currency { get; set; } = "AFN";
    public string Type { get; set; } = "دریافتی";
    public string Correspondent { get; set; } = "کویته";
    public string PaymentLocation { get; set; } = "خود صرافی";
    public decimal? Amount { get; set; }
    public DateTime Date { get; set; } = new(2026,10,18);
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if(Section=="hawalas" && string.IsNullOrWhiteSpace(OtherName))
            yield return new("نام گیرنده را وارد کنید.", [nameof(OtherName)]);
        if(Section=="hawalas" && (!Amount.HasValue || Amount<=0))
            yield return new("مبلغ باید بیشتر از صفر باشد.", [nameof(Amount)]);
    }
}
