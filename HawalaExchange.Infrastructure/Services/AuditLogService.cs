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
    }
}