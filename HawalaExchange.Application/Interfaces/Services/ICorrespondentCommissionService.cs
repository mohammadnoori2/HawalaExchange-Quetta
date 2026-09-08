using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ICorrespondentCommissionService
{
    Task<CorrespondentCommissionPreviewDto> PreviewAsync(CorrespondentCommissionPreviewRequestDto request, CancellationToken cancellationToken = default);
    Task<CorrespondentCommissionBatchDto> PostAsync(CorrespondentCommissionPreviewRequestDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrespondentCommissionBatchDto>> GetHistoryAsync(long correspondentId, CancellationToken cancellationToken = default);
    Task ReverseAsync(long batchId, string reason, CancellationToken cancellationToken = default);
}
