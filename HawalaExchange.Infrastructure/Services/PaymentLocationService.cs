using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class PaymentLocationService : IPaymentLocationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IAuditLogService _auditLogService;

        public PaymentLocationService(
            ApplicationDbContext context,
            IMapper mapper,
            IAuditLogService auditLogService)
        {
            _context = context;
            _mapper = mapper;
            _auditLogService = auditLogService;
        }

        public async Task<IEnumerable<PaymentLocationDto>> GetAllAsync()
        {
            var entities = await _context.PaymentLocations
                .Include(p => p.CreatedByUser)
                .Include(p => p.Correspondent)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return _mapper.Map<IEnumerable<PaymentLocationDto>>(entities);
        }

        public async Task<IEnumerable<PaymentLocationDto>> GetActiveAsync()
        {
            var entities = await _context.PaymentLocations
                .Where(p => p.IsActive)
                .Include(p => p.Correspondent)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return _mapper.Map<IEnumerable<PaymentLocationDto>>(entities);
        }

        public async Task<IEnumerable<PaymentLocationDto>> GetByCorrespondentAsync(
            long correspondentId,
            bool includeInactive = false)
        {
            if (correspondentId <= 0)
                return [];

            var query = _context.PaymentLocations
                .AsNoTracking()
                .Where(p => p.CorrespondentId == correspondentId);

            if (!includeInactive)
                query = query.Where(p => p.IsActive);

            var entities = await query
                .Include(p => p.Correspondent)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return _mapper.Map<IEnumerable<PaymentLocationDto>>(entities);
        }

        public async Task<PaymentLocationDto?> GetByIdAsync(long id)
        {
            var entity = await _context.PaymentLocations
                .Include(p => p.CreatedByUser)
                .Include(p => p.Correspondent)
                .FirstOrDefaultAsync(p => p.Id == id);

            return entity == null ? null : _mapper.Map<PaymentLocationDto>(entity);
        }

        public async Task<PaymentLocationDto> CreateAsync(CreatePaymentLocationDto dto)
        {
            await EnsureValidCorrespondentAsync(dto.CorrespondentId);

            var entity = _mapper.Map<PaymentLocation>(dto);
            entity.CreatedAt = DateTime.UtcNow;
            entity.CreatedBy = GetCurrentUserId();

            await _context.PaymentLocations.AddAsync(entity);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "CREATE",
                "PaymentLocations",
                entity.Id,
                null,
                $"آدرس '{entity.Name}' ایجاد شد",
                GetCurrentUserId()
            );

            return _mapper.Map<PaymentLocationDto>(entity);
        }

        public async Task<PaymentLocationDto> UpdateAsync(long id, UpdatePaymentLocationDto dto)
        {
            await EnsureValidCorrespondentAsync(dto.CorrespondentId);

            var entity = await _context.PaymentLocations.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"آدرس با شناسه {id} یافت نشد.");

            _mapper.Map(dto, entity);
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "UPDATE",
                "PaymentLocations",
                entity.Id,
                null,
                $"آدرس '{entity.Name}' ویرایش شد",
                GetCurrentUserId()
            );

            return _mapper.Map<PaymentLocationDto>(entity);
        }

        public async Task DeleteAsync(long id)
        {
            var entity = await _context.PaymentLocations.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"آدرس با شناسه {id} یافت نشد.");

            // بررسی اینکه آیا این آدرس در حواله‌ها استفاده شده است
            var isUsed = await _context.Hawalas.AnyAsync(h => h.PaymentLocationId == id);
            if (isUsed)
                throw new InvalidOperationException("این آدرس در حواله‌ها استفاده شده است و قابل حذف نیست.");

            _context.PaymentLocations.Remove(entity);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "DELETE",
                "PaymentLocations",
                id,
                null,
                $"آدرس '{entity.Name}' حذف شد",
                GetCurrentUserId()
            );
        }

        public async Task<PaymentLocationDto> ToggleActiveAsync(long id)
        {
            var entity = await _context.PaymentLocations.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"آدرس با شناسه {id} یافت نشد.");

            entity.IsActive = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "UPDATE",
                "PaymentLocations",
                entity.Id,
                null,
                $"وضعیت آدرس '{entity.Name}' به {(entity.IsActive ? "فعال" : "غیرفعال")} تغییر یافت",
                GetCurrentUserId()
            );

            return _mapper.Map<PaymentLocationDto>(entity);
        }

        private long GetCurrentUserId() => _context.RequireCurrentUserId();

        private async Task EnsureValidCorrespondentAsync(long? correspondentId)
        {
            if (!correspondentId.HasValue || correspondentId.Value <= 0)
                throw new InvalidOperationException("انتخاب نمایندگی برای محل پرداخت الزامی است.");

            var exists = await _context.Correspondents
                .AsNoTracking()
                .AnyAsync(c => c.Id == correspondentId.Value && !c.IsArchived);

            if (!exists)
                throw new InvalidOperationException("نمایندگی انتخاب‌شده معتبر یا فعال نیست.");
        }
    }
}
