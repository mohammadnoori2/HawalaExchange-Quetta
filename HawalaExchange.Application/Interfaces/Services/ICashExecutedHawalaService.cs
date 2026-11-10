using HawalaExchange.Application.DTOs;
namespace HawalaExchange.Application.Interfaces.Services;
public interface ICashExecutedHawalaService
{
    Task<CashExecutedHawalaLookups> GetLookupsAsync(CancellationToken cancellationToken = default);
    Task<CashExecutedHawalaResult> GetAsync(CashExecutedHawalaFilter filter, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<SelectedRecordsFileDto> ExportAsync(CashExecutedHawalaFilter filter, ExportFormat format, CancellationToken cancellationToken = default);
}
