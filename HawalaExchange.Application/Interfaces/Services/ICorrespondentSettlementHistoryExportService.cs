using HawalaExchange.Application.DTOs;
namespace HawalaExchange.Application.Interfaces.Services;

public interface ICorrespondentSettlementHistoryExportService
{
    Task<SelectedRecordsFileDto> ExportAsync(long correspondentId,long conversionId,ExportFormat format);
}
