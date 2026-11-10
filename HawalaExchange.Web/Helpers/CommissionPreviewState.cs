using System.Text.Json;
using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Web.Helpers;

/// <summary>Keeps the accounting request tied to the last successful preview.</summary>
public sealed class CommissionPreviewState
{
    private string? acceptedRequest;

    public void Accept(CorrespondentCommissionPreviewRequestDto request) =>
        acceptedRequest = JsonSerializer.Serialize(request);

    public void Invalidate() => acceptedRequest = null;

    public bool IsCurrent(CorrespondentCommissionPreviewRequestDto request) =>
        acceptedRequest is not null && acceptedRequest == JsonSerializer.Serialize(request);

    public CorrespondentCommissionPreviewRequestDto ForPosting(CorrespondentCommissionPreviewRequestDto request)
    {
        if (!IsCurrent(request))
            throw new InvalidOperationException("نرخ‌ها یا دوره تغییر کرده‌اند؛ ابتدا کمیشن را باز‌محاسبه کنید.");
        return Copy(request);
    }

    public static CorrespondentCommissionPreviewRequestDto Copy(CorrespondentCommissionPreviewRequestDto request) =>
        JsonSerializer.Deserialize<CorrespondentCommissionPreviewRequestDto>(JsonSerializer.Serialize(request))!;
}
