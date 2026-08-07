using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class PlatformAuditService(ApplicationDbContext context) : IPlatformAuditService
{
    public async Task<PlatformAuditPageDto> GetAsync(
        PlatformAuditFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var query = context.PlatformAuditLogs.AsNoTracking().AsQueryable();
        if (filter.FromDate.HasValue)
        {
            var from = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt >= from);
        }
        if (filter.ToDate.HasValue)
        {
            var to = DateTime.SpecifyKind(filter.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt < to);
        }
        if (filter.TenantId.HasValue)
            query = query.Where(x => x.TenantId == filter.TenantId);
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(x => x.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Action.Contains(term) || x.EntityName.Contains(term) ||
                (x.Details != null && x.Details.Contains(term)) ||
                (x.Tenant != null && x.Tenant.Name.Contains(term)));
        }

        var totalRows = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 10, 100);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PlatformAuditRowDto
            {
                Id = x.Id,
                ActorUserId = x.ActorUserId,
                ActorName = x.ActorUserId.HasValue
                    ? context.Users.IgnoreQueryFilters().Where(u => u.Id == x.ActorUserId.Value)
                        .Select(u => u.FullName).FirstOrDefault() ?? "کاربر حذف‌شده"
                    : "سیستم",
                TenantId = x.TenantId,
                TenantName = x.Tenant != null ? x.Tenant.Name : null,
                Action = x.Action,
                EntityName = x.EntityName,
                EntityId = x.EntityId,
                Details = x.Details,
                CreatedAt = x.CreatedAt
            }).ToListAsync(cancellationToken);

        return new PlatformAuditPageDto { TotalRows = totalRows, Rows = rows };
    }

    public async Task<IReadOnlyList<string>> GetActionsAsync(CancellationToken cancellationToken = default) =>
        await context.PlatformAuditLogs.AsNoTracking().Select(x => x.Action).Distinct()
            .OrderBy(x => x).ToListAsync(cancellationToken);
}
