using HawalaExchange.Application.DTOs;
namespace HawalaExchange.Application.Interfaces.Services;
public interface IHawalaCommissionReportService
{
    Task<HawalaCommissionReportResult> GetAsync(HawalaCommissionReportFilter filter, CancellationToken cancellationToken = default);
    Task<SelectedRecordsFileDto> ExportAsync(HawalaCommissionReportFilter filter, CancellationToken cancellationToken = default);
}
