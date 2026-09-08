using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IHawalaImportService
    {
        Task<HawalaImportPreviewDto> PreviewAsync(
            Stream file,
            string fileName,
            long correspondentId,
            CancellationToken cancellationToken = default);

        Task<HawalaImportResultDto> ConfirmAsync(
            ConfirmHawalaImportDto request,
            CancellationToken cancellationToken = default);
    }
}
