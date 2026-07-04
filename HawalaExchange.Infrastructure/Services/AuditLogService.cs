using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public AuditLogService(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<AuditLogDto> LogAsync(string action, string tableName, long recordId, string? oldValue = null, string? newValue = null, long? userId = null)
        {
            var log = new AuditLog
            {
                UserId = userId,
                Action = action,
                TableName = tableName,
                RecordId = recordId,
                OldValue = oldValue,
                NewValue = newValue,
                CreatedAt = DateTime.UtcNow
            };
            await _context.AuditLogs.AddAsync(log);
            await _context.SaveChangesAsync();
            return _mapper.Map<AuditLogDto>(log);
        }

        public async Task<IEnumerable<AuditLogDto>> GetLogsByTableAsync(string tableName, long recordId)
        {
            var logs = await _context.AuditLogs
                .Where(l => l.TableName == tableName && l.RecordId == recordId)
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AuditLogDto>>(logs);
        }

        public async Task<IEnumerable<AuditLogDto>> GetLogsByUserAsync(long userId)
        {
            var logs = await _context.AuditLogs
                .Where(l => l.UserId == userId)
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AuditLogDto>>(logs);
        }

        public async Task<IEnumerable<AuditLogDto>> GetLogsByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            var logs = await _context.AuditLogs
                .Where(l => l.CreatedAt >= fromDate && l.CreatedAt <= toDate)
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AuditLogDto>>(logs);
        }

        public async Task<IEnumerable<AuditLogDto>> GetLogsByActionAsync(string action)
        {
            var logs = await _context.AuditLogs
                .Where(l => l.Action == action)
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AuditLogDto>>(logs);
        }

        public async Task<IEnumerable<AuditLogDto>> GetAllLogsAsync()
        {
            var logs = await _context.AuditLogs
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AuditLogDto>>(logs);
        }

        public async Task<PaginatedResult<AuditLogDto>> GetFilteredLogsAsync(AuditLogFilterDto filter)
        {
            var query = _context.AuditLogs
                .Include(l => l.User)
                .AsQueryable();

            if (filter.FromDate.HasValue)
                query = query.Where(l => l.CreatedAt >= filter.FromDate.Value);

            if (filter.ToDate.HasValue)
                query = query.Where(l => l.CreatedAt <= filter.ToDate.Value);

            if (filter.UserId.HasValue && filter.UserId.Value > 0)
                query = query.Where(l => l.UserId == filter.UserId.Value);

            if (!string.IsNullOrEmpty(filter.Action))
                query = query.Where(l => l.Action == filter.Action);

            if (!string.IsNullOrEmpty(filter.TableName))
                query = query.Where(l => l.TableName.Contains(filter.TableName));

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(l =>
                    l.TableName.ToLower().Contains(term) ||
                    (l.OldValue != null && l.OldValue.ToLower().Contains(term)) ||
                    (l.NewValue != null && l.NewValue.ToLower().Contains(term))
                );
            }

            var totalCount = await query.CountAsync();

            if (filter.PageSize > 0)
            {
                query = query.Skip((filter.PageNumber - 1) * filter.PageSize)
                             .Take(filter.PageSize);
            }

            var items = await query.ToListAsync();
            var dtos = _mapper.Map<IEnumerable<AuditLogDto>>(items);

            return new PaginatedResult<AuditLogDto>
            {
                Items = dtos,
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
                TotalPages = filter.PageSize > 0
                    ? (int)Math.Ceiling((double)totalCount / filter.PageSize)
                    : 1
            };
        }

        public async Task<IEnumerable<AuditLogDto>> GetFilteredLogsNoPagingAsync(
            DateTime? fromDate = null,
            DateTime? toDate = null,
            long? userId = null,
            string? action = null,
            string? tableName = null,
            string? searchTerm = null)
        {
            var query = _context.AuditLogs
                .Include(l => l.User)
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(l => l.CreatedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(l => l.CreatedAt <= toDate.Value);

            if (userId.HasValue && userId.Value > 0)
                query = query.Where(l => l.UserId == userId.Value);

            if (!string.IsNullOrEmpty(action))
                query = query.Where(l => l.Action == action);

            if (!string.IsNullOrEmpty(tableName))
                query = query.Where(l => l.TableName.Contains(tableName));

            if (!string.IsNullOrEmpty(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(l =>
                    l.TableName.ToLower().Contains(term) ||
                    (l.OldValue != null && l.OldValue.ToLower().Contains(term)) ||
                    (l.NewValue != null && l.NewValue.ToLower().Contains(term))
                );
            }

            var logs = await query
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return _mapper.Map<IEnumerable<AuditLogDto>>(logs);
        }

        public async Task<AuditLogStatisticsDto> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.AuditLogs.AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(l => l.CreatedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(l => l.CreatedAt <= toDate.Value);

            var statistics = new AuditLogStatisticsDto
            {
                TotalLogs = await query.CountAsync(),
                CreateCount = await query.CountAsync(l => l.Action == "CREATE"),
                UpdateCount = await query.CountAsync(l => l.Action == "UPDATE"),
                DeleteCount = await query.CountAsync(l => l.Action == "DELETE"),
                CancelCount = await query.CountAsync(l => l.Action == "CANCEL"),
                ReverseCount = await query.CountAsync(l => l.Action == "REVERSE"),
                TodayLogs = await query.CountAsync(l => l.CreatedAt.Date == DateTime.UtcNow.Date),
                TopUsers = await query
                    .GroupBy(l => l.UserId)
                    .Select(g => new UserActivityDto
                    {
                        UserId = g.Key ?? 0,
                        UserName = g.FirstOrDefault().User != null ? g.FirstOrDefault().User.FullName : "سیستم",
                        LogCount = g.Count()
                    })
                    .OrderByDescending(u => u.LogCount)
                    .Take(5)
                    .ToListAsync()
            };

            return statistics;
        }

        public async Task<IEnumerable<string>> GetDistinctActionsAsync()
        {
            return await _context.AuditLogs
                .Select(l => l.Action)
                .Distinct()
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetDistinctTablesAsync()
        {
            return await _context.AuditLogs
                .Select(l => l.TableName)
                .Distinct()
                .ToListAsync();
        }

        public async Task<int> CleanupOldLogsAsync(int daysToKeep = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            var oldLogs = await _context.AuditLogs
                .Where(l => l.CreatedAt < cutoffDate)
                .ToListAsync();

            if (oldLogs.Any())
            {
                _context.AuditLogs.RemoveRange(oldLogs);
                await _context.SaveChangesAsync();
            }

            return oldLogs.Count;
        }
    }
}