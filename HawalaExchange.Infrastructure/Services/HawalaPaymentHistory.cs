using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    private async Task SavePaymentBatchAsync(List<Hawala> hawalas, BulkHawalaPaymentPreviewDto preview)
    {
        var correspondentIds = hawalas.Where(x => x.CorrespondentId.HasValue).Select(x => x.CorrespondentId!.Value).Distinct().ToArray();
        var locationIds = hawalas.Where(x => x.PaymentLocationId.HasValue).Select(x => x.PaymentLocationId!.Value).Distinct().ToArray();
        var correspondents = await _context.Correspondents.AsNoTracking().Where(x => correspondentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var locations = await _context.PaymentLocations.AsNoTracking().Where(x => locationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var codes = preview.Totals.ToDictionary(x => x.CurrencyId, x => x.CurrencyCode);
        var userId = GetCurrentUserId();
        var batch = new HawalaPaymentBatch
        {
            ExecutedAt = DateTime.UtcNow, ExecutedBy = userId, PaidFromAccountId = preview.PaidFromAccountId,
            AccountName = preview.AccountName,
            ExecutedByName = await _context.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.FullName ?? x.UserName ?? "").FirstOrDefaultAsync() ?? "",
            Items = hawalas.Select(h => new HawalaPaymentBatchItem
            {
                HawalaId = h.Id, Number = h.Number, ReferenceNumber = h.ReferenceNumber ?? "",
                SenderName = h.SenderName ?? "", ReceiverName = h.ReceiverName ?? "",
                CorrespondentName = h.CorrespondentId.HasValue ? correspondents.GetValueOrDefault(h.CorrespondentId.Value, "") : "",
                PaymentLocation = h.PaymentLocationId.HasValue ? locations.GetValueOrDefault(h.PaymentLocationId.Value, "") : "",
                RegisteredAt = h.CreatedAt, CurrencyId = h.ToCurrencyId, CurrencyCode = codes.GetValueOrDefault(h.ToCurrencyId, ""),
                Amount = h.ToAmount ?? h.FromAmount, AgentCommission = h.AgentCommissionAmount ?? 0,
                CommissionCurrencyId = h.AgentCommissionCurrencyId,
                CommissionCurrencyCode = h.AgentCommissionCurrencyId.HasValue ? codes.GetValueOrDefault(h.AgentCommissionCurrencyId.Value, "") : ""
            }).ToList()
        };
        _context.HawalaPaymentBatches.Add(batch);
        await _context.SaveChangesAsync();
    }

    public async Task<PaginatedResult<HawalaPaymentBatchDto>> GetPaymentHistoryAsync(int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.HawalaPaymentBatches.AsNoTracking();
        var count = await query.CountAsync();
        return new PaginatedResult<HawalaPaymentBatchDto>
        {
            TotalCount = count, PageNumber = page, PageSize = pageSize, TotalPages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize)),
            Items = await query.OrderByDescending(x => x.ExecutedAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).Select(x => new HawalaPaymentBatchDto
                { Id = x.Id, ExecutedAt = x.ExecutedAt, AccountName = x.AccountName, ExecutedByName = x.ExecutedByName, Count = x.Items.Count }).ToListAsync()
        };
    }

    public async Task<HawalaPaymentBatchDto> GetPaymentBatchAsync(long id)
    {
        var batch = await _context.HawalaPaymentBatches.AsNoTracking().Include(x => x.Items).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("اجرای گروهی موردنظر یافت نشد یا در دسترس شما نیست.");
        var dto = new HawalaPaymentBatchDto
        {
            Id = batch.Id, ExecutedAt = batch.ExecutedAt, AccountName = batch.AccountName, ExecutedByName = batch.ExecutedByName, Count = batch.Items.Count,
            Items = batch.Items.OrderBy(x => x.CurrencyCode).ThenBy(x => x.Number).Select(x => new HawalaPaymentBatchItemDto
            {
                HawalaId = x.HawalaId, Number = x.Number, ReferenceNumber = x.ReferenceNumber, SenderName = x.SenderName, ReceiverName = x.ReceiverName,
                CorrespondentName = x.CorrespondentName, PaymentLocation = x.PaymentLocation, RegisteredAt = x.RegisteredAt,
                CurrencyId = x.CurrencyId, CurrencyCode = x.CurrencyCode, Amount = x.Amount,
                AgentCommission = x.AgentCommission, CommissionCurrencyId = x.CommissionCurrencyId, CommissionCurrencyCode = x.CommissionCurrencyCode
            }).ToList()
        };
        dto.Totals = dto.Items.Select(x => new BulkHawalaPaymentCurrencyDto { CurrencyId = x.CurrencyId, CurrencyCode = x.CurrencyCode, Principal = x.Amount })
            .Concat(dto.Items.Where(x => x.AgentCommission > 0 && x.CommissionCurrencyId.HasValue).Select(x => new BulkHawalaPaymentCurrencyDto
            { CurrencyId = x.CommissionCurrencyId!.Value, CurrencyCode = x.CommissionCurrencyCode, AgentCommission = x.AgentCommission }))
            .GroupBy(x => new { x.CurrencyId, x.CurrencyCode }).Select(g => new BulkHawalaPaymentCurrencyDto
            { CurrencyId = g.Key.CurrencyId, CurrencyCode = g.Key.CurrencyCode, Principal = g.Sum(x => x.Principal), AgentCommission = g.Sum(x => x.AgentCommission) }).OrderBy(x => x.CurrencyCode).ToList();
        return dto;
    }
}
