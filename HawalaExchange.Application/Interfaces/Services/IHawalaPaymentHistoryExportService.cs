using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface IHawalaPaymentHistoryExportService
{
    Task<SelectedRecordsFileDto> ExportAsync(long batchId, ExportFormat format);
}
