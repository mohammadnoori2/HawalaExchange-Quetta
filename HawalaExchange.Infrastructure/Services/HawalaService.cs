using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class HawalaService : IHawalaService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public HawalaService(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<PaginatedResult<HawalaDto>> GetHawalasAsync(HawalaFilterDto filter)
        {
            var query = _context.Hawalas
                .Include(h => h.Transaction)
                .Include(h => h.Branch)
                .Include(h => h.Customer)
                .Include(h => h.Correspondent)
                .Include(h => h.FromCurrency)
                .Include(h => h.ToCurrency)
                .Include(h => h.CommissionCurrency)
                .Include(h => h.AgentCommissionCurrency)
                .Include(h => h.CreatedByUser)
                .Include(h => h.PaidByUser)
                .Include(h => h.CancelledByUser)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(h =>
                    (h.Transaction != null && h.Transaction.TransactionNo.ToLower().Contains(term)) ||
                    (h.SenderName != null && h.SenderName.ToLower().Contains(term)) ||
                    (h.ReceiverName != null && h.ReceiverName.ToLower().Contains(term)) ||
                    (h.ReferenceNumber != null && h.ReferenceNumber.ToLower().Contains(term))
                );
            }

            if (!string.IsNullOrEmpty(filter.HawalaType))
                query = query.Where(h => h.HawalaType == filter.HawalaType);

            if (!string.IsNullOrEmpty(filter.Status))
                query = query.Where(h => h.Status == filter.Status);

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
                query = query.Where(h => h.BranchId == filter.BranchId.Value);

            if (filter.CustomerId.HasValue && filter.CustomerId.Value > 0)
                query = query.Where(h => h.CustomerId == filter.CustomerId.Value);

            if (filter.CorrespondentId.HasValue && filter.CorrespondentId.Value > 0)
                query = query.Where(h => h.CorrespondentId == filter.CorrespondentId.Value);

            if (filter.FromDate.HasValue)
                query = query.Where(h => h.CreatedAt >= filter.FromDate.Value);

            if (filter.ToDate.HasValue)
                query = query.Where(h => h.CreatedAt <= filter.ToDate.Value);

            query = filter.SortDirection == "asc"
                ? query.OrderBy(h => EF.Property<object>(h, filter.SortColumn ?? "CreatedAt"))
                : query.OrderByDescending(h => EF.Property<object>(h, filter.SortColumn ?? "CreatedAt"));

            var totalCount = await query.CountAsync();

            if (filter.PageSize > 0)
            {
                query = query.Skip((filter.PageNumber - 1) * filter.PageSize)
                             .Take(filter.PageSize);
            }

            var items = await query.ToListAsync();
            var dtos = _mapper.Map<IEnumerable<HawalaDto>>(items);

            return new PaginatedResult<HawalaDto>
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

        public async Task<HawalaDto> GetHawalaByIdAsync(long id)
        {
            var hawala = await _context.Hawalas
                .Include(h => h.Transaction)
                .Include(h => h.Branch)
                .Include(h => h.Customer)
                .Include(h => h.Correspondent)
                .Include(h => h.FromCurrency)
                .Include(h => h.ToCurrency)
                .Include(h => h.CommissionCurrency)
                .Include(h => h.AgentCommissionCurrency)
                .Include(h => h.CreatedByUser)
                .Include(h => h.PaidByUser)
                .Include(h => h.CancelledByUser)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (hawala == null)
                throw new KeyNotFoundException("حواله مورد نظر یافت نشد");

            return _mapper.Map<HawalaDto>(hawala);
        }

        public async Task<HawalaDto> CreateHawalaAsync(CreateHawalaDto createDto)
        {
            try
            {
                // 1. اعتبارسنجی
                if (string.IsNullOrEmpty(createDto.HawalaType))
                    throw new Exception("نوع حواله الزامی است");

                if (createDto.BranchId == 0)
                    throw new Exception("شعبه الزامی است");

                if (createDto.FromCurrencyId == 0)
                    throw new Exception("ارز مبدأ الزامی است");

                if (createDto.FromAmount <= 0)
                    throw new Exception("مبلغ باید بزرگتر از صفر باشد");

                if (createDto.ToCurrencyId == 0)
                    throw new Exception("ارز مقصد الزامی است");

                // 2. ایجاد تراکنش
                var transaction = new Transaction
                {
                    TransactionNo = await GenerateTransactionNumberAsync(),
                    TransactionType = createDto.HawalaType,
                    BranchId = createDto.BranchId,
                    CustomerId = createDto.CustomerId,
                    Status = createDto.Status ?? "Pending",
                    CreatedBy = 1,
                    CreatedAt = DateTime.UtcNow,
                    Remarks = $"حواله {createDto.HawalaType}"
                };

                await _context.Transactions.AddAsync(transaction);
                await _context.SaveChangesAsync();

                // 3. ایجاد حواله با TransactionId
                var hawala = _mapper.Map<Hawala>(createDto);
                hawala.TransactionId = transaction.Id;
                hawala.CreatedAt = DateTime.UtcNow;
                hawala.CreatedBy = 1;
                hawala.Status = createDto.Status ?? "Pending";

                // ===================================================
                // ✅ مهم: مقادیر 0 را به null تبدیل کنید
                // ===================================================
                if (hawala.ExchangeRate == 0 || hawala.ExchangeRate == null)
                {
                    hawala.ExchangeRate = null;
                }

                if (hawala.ToAmount == 0 || hawala.ToAmount == null)
                {
                    hawala.ToAmount = null;
                }

                if (hawala.CommissionAmount == 0 || hawala.CommissionAmount == null)
                {
                    hawala.CommissionAmount = null;
                }

                if (hawala.AgentCommissionAmount == 0 || hawala.AgentCommissionAmount == null)
                {
                    hawala.AgentCommissionAmount = null;
                }

                if (hawala.CommissionCurrencyId == 0 || hawala.CommissionCurrencyId == null)
                {
                    hawala.CommissionCurrencyId = null;
                }

                if (hawala.AgentCommissionCurrencyId == 0 || hawala.AgentCommissionCurrencyId == null)
                {
                    hawala.AgentCommissionCurrencyId = null;
                }
                // ===================================================

                await _context.Hawalas.AddAsync(hawala);
                await _context.SaveChangesAsync();

                return await GetHawalaByIdAsync(hawala.Id);
            }
            catch (DbUpdateException ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                throw new Exception($"خطا در ذخیره‌سازی: {innerMessage}");
            }
        }

        private async Task<string> GenerateTransactionNumberAsync()
        {
            var datePart = DateTime.Now.ToString("yyyyMMdd");
            var lastTransaction = await _context.Transactions
                .Where(t => t.TransactionNo.StartsWith($"HWL-{datePart}"))
                .OrderByDescending(t => t.TransactionNo)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastTransaction != null)
            {
                var parts = lastTransaction.TransactionNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
            }

            return $"HWL-{datePart}-{nextNumber:D4}";
        }

        public async Task<HawalaDto> UpdateHawalaAsync(long id, UpdateHawalaDto updateDto)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException("حواله مورد نظر یافت نشد");

            _mapper.Map(updateDto, hawala);

            // ===================================================
            // ✅ در ویرایش نیز مقادیر 0 را به null تبدیل کنید
            // ===================================================
            if (hawala.ExchangeRate == 0 || hawala.ExchangeRate == null)
            {
                hawala.ExchangeRate = null;
            }

            if (hawala.ToAmount == 0 || hawala.ToAmount == null)
            {
                hawala.ToAmount = null;
            }

            if (hawala.CommissionAmount == 0 || hawala.CommissionAmount == null)
            {
                hawala.CommissionAmount = null;
            }

            if (hawala.AgentCommissionAmount == 0 || hawala.AgentCommissionAmount == null)
            {
                hawala.AgentCommissionAmount = null;
            }

            if (hawala.CommissionCurrencyId == 0 || hawala.CommissionCurrencyId == null)
            {
                hawala.CommissionCurrencyId = null;
            }

            if (hawala.AgentCommissionCurrencyId == 0 || hawala.AgentCommissionCurrencyId == null)
            {
                hawala.AgentCommissionCurrencyId = null;
            }
            // ===================================================

            await _context.SaveChangesAsync();

            return await GetHawalaByIdAsync(id);
        }

        public async Task<HawalaDto> MarkAsPaidAsync(long id, long userId)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException("حواله مورد نظر یافت نشد");

            hawala.Status = "Paid";
            hawala.PaidAt = DateTime.UtcNow;
            hawala.PaidBy = userId;

            await _context.SaveChangesAsync();
            return await GetHawalaByIdAsync(id);
        }

        public async Task<HawalaDto> CancelHawalaAsync(long id, string cancelReason, long userId)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException("حواله مورد نظر یافت نشد");

            if (hawala.Status == "Paid")
                throw new InvalidOperationException("حواله پرداخت شده قابل لغو نیست");

            hawala.Status = "Cancel";
            hawala.CancelledAt = DateTime.UtcNow;
            hawala.CancelledBy = userId;
            hawala.CancelReason = cancelReason;

            await _context.SaveChangesAsync();
            return await GetHawalaByIdAsync(id);
        }

        public async Task<HawalaStatisticsDto> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.Hawalas.AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(h => h.CreatedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(h => h.CreatedAt <= toDate.Value);

            var statistics = new HawalaStatisticsDto
            {
                TotalHawalas = await query.CountAsync(),
                HawalaSendCount = await query.CountAsync(h => h.HawalaType == "HawalaSend"),
                HawalaReceiveCount = await query.CountAsync(h => h.HawalaType == "HawalaReceive"),
                HawalaOtherCount = await query.CountAsync(h => h.HawalaType == "HawalaOther"),
                PendingCount = await query.CountAsync(h => h.Status == "Pending"),
                PaidCount = await query.CountAsync(h => h.Status == "Paid"),
                CancelCount = await query.CountAsync(h => h.Status == "Cancel"),
                TotalAmount = await query.SumAsync(h => h.FromAmount),
                TotalCommission = await query.SumAsync(h => h.CommissionAmount ?? 0),
                DailyStats = await query
                    .GroupBy(h => h.CreatedAt.Date)
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.Date.ToString("yyyy-MM-dd"), v => v.Count)
            };

            return statistics;
        }

        public async Task<IEnumerable<string>> GetDistinctHawalaTypesAsync()
        {
            return await _context.Hawalas
                .Select(h => h.HawalaType)
                .Distinct()
                .ToListAsync();
        }

        public async Task<int> DeleteHawalaAsync(long id)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException("حواله مورد نظر یافت نشد");

            _context.Hawalas.Remove(hawala);
            return await _context.SaveChangesAsync();
        }
    }
}