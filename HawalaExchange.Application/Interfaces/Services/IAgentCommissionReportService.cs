using HawalaExchange.Application.DTOs;
namespace HawalaExchange.Application.Interfaces.Services;
public interface IAgentCommissionReportService
{
    Task<AgentCommissionReportLookups> GetLookupsAsync(long correspondentId, CancellationToken cancellationToken = default);
    Task<AgentCommissionReportResult> GetAsync(AgentCommissionReportFilter filter, CancellationToken cancellationToken = default);
    Task<PaginatedResult<AgentCommissionReportRow>> GetDetailsAsync(AgentCommissionReportFilter filter, long? locationId = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<SelectedRecordsFileDto> ExportAsync(AgentCommissionReportFilter filter, ExportFormat format, CancellationToken cancellationToken = default);
}
