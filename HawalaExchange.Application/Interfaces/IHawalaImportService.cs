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
    }
}
