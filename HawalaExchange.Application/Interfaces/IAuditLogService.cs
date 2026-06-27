using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IAuditLogService
    {
        Task<AuditLogDto> LogAsync(string action, string tableName, long recordId, string? oldValue = null, string? newValue = null, long? userId = null);
        Task<IEnumerable<AuditLogDto>> GetLogsByTableAsync(string tableName, long recordId);
        Task<IEnumerable<AuditLogDto>> GetLogsByUserAsync(long userId);
        Task<IEnumerable<AuditLogDto>> GetLogsByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<IEnumerable<AuditLogDto>> GetLogsByActionAsync(string action);
        Task<IEnumerable<AuditLogDto>> GetAllLogsAsync();
    }
}