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

        Task<PaginatedResult<AuditLogDto>> GetFilteredLogsAsync(AuditLogFilterDto filter);
        Task<IEnumerable<AuditLogDto>> GetFilteredLogsNoPagingAsync(DateTime? fromDate = null, DateTime? toDate = null, long? userId = null, string? action = null, string? tableName = null, string? searchTerm = null);
        Task<AuditLogStatisticsDto> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
        Task<IEnumerable<string>> GetDistinctActionsAsync();
        Task<IEnumerable<string>> GetDistinctTablesAsync();
        Task<int> CleanupOldLogsAsync(int daysToKeep = 30);
    }
}