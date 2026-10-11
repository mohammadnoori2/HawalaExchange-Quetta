using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IExportService
    {
        Task<CustomerActivityReportDto> GetCustomerActivitiesPreviewAsync(ExportFilterDto filter);
        Task<(byte[] Content, string ContentType, string FileName)> ExportCustomerActivitiesPreviewAsync(CustomerActivityReportDto report, ExportFormat format);
        Task<(byte[] Content, string ContentType, string FileName)> ExportCustomerActivitiesAsync(ExportFilterDto filter, ExportFormat format);
    }
}
