using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IAedDealService
{
    Task<IReadOnlyList<AedDealDto>> GetAllAsync(long? correspondentId = null, CancellationToken cancellationToken = default);
    Task<AedDealDto> CreateAsync(CreateAedDealDto dto, CancellationToken cancellationToken = default);
    Task<AedConversionPreviewDto> PreviewConversionAsync(PreviewAedConversionDto dto, CancellationToken cancellationToken = default);
    Task<AedDealDto> ConvertAsync(PreviewAedConversionDto dto, CancellationToken cancellationToken = default);
    Task ReverseConversionAsync(long conversionId, string reason, CancellationToken cancellationToken = default);
    Task CancelAsync(long dealId, string reason, CancellationToken cancellationToken = default);
}
