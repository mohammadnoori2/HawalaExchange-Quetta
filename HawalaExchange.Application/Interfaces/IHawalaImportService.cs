using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IHawalaImportService
    {
        Task<HawalaImportPreviewDto> PreviewAsync(
            Stream file,
            string fileName,
            long correspondentId,
            IProgress<HawalaImportProgressDto>? progress = null,
            CancellationToken cancellationToken = default);

        Task<HawalaImportResultDto> ConfirmAsync(
            ConfirmHawalaImportDto request,
            IProgress<HawalaImportProgressDto>? progress = null,
            CancellationToken cancellationToken = default);

        Task<HawalaImportPreviewDto?> GetPreviewAsync(
            long batchId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<HawalaImportHistoryDto>> GetHistoryAsync(
            CancellationToken cancellationToken = default);

        Task<HawalaImportDetailsDto?> GetDetailsAsync(
            long batchId,
            CancellationToken cancellationToken = default);

        Task DeleteBatchAsync(
            long batchId,
            CancellationToken cancellationToken = default);
    }
}
