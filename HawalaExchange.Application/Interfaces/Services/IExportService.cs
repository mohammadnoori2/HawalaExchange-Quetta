using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IExportService
    {
        Task<(byte[] Content, string ContentType, string FileName)> ExportCustomerActivitiesAsync(ExportFilterDto filter, ExportFormat format);
    }
}
