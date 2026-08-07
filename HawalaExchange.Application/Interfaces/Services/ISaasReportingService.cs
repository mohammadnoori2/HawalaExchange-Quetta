using HawalaExchange.Application.DTOs;
namespace HawalaExchange.Application.Interfaces.Services;
public interface ISaasReportingService
{
    Task<SaasManagementReportDto> GetReportAsync(SaasReportFilterDto filter, CancellationToken cancellationToken = default);
    Task<SaasReportExportDto> ExportExcelAsync(SaasReportFilterDto filter, CancellationToken cancellationToken = default);
}
