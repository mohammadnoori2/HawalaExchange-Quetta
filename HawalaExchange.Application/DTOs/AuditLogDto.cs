using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class AuditLogDto
    {
        public long Id { get; set; }
        public long? UserId { get; set; }
        public string? UserName { get; set; }
        public string Action { get; set; }
        public string TableName { get; set; }
        public long RecordId { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AuditLogFilterDto
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public long? UserId { get; set; }
        public string? Action { get; set; }
        public string? TableName { get; set; }
        public string? SearchTerm { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SortColumn { get; set; } = "CreatedAt";
        public string SortDirection { get; set; } = "desc";
    }

    public class PaginatedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }

    public class AuditLogStatisticsDto
    {
        public int TotalLogs { get; set; }
        public int CreateCount { get; set; }
        public int UpdateCount { get; set; }
        public int DeleteCount { get; set; }
        public int CancelCount { get; set; }
        public int ReverseCount { get; set; }
        public int TodayLogs { get; set; }
        public List<UserActivityDto> TopUsers { get; set; } = new();
    }

    public class UserActivityDto
    {
        public long UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int LogCount { get; set; }
    }
}