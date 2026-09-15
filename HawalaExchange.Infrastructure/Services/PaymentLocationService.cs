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
                .Include(p => p.Aliases)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return _mapper.Map<IEnumerable<PaymentLocationDto>>(entities);
        }

        public async Task<IEnumerable<PaymentLocationDto>> GetActiveAsync()
        {
            var entities = await _context.PaymentLocations
                .Where(p => p.IsActive)
                .Include(p => p.Aliases)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return _mapper.Map<IEnumerable<PaymentLocationDto>>(entities);
        }

        public async Task<PaymentLocationDto?> GetByIdAsync(long id)
        {
            var entity = await _context.PaymentLocations
                .Include(p => p.CreatedByUser)
                .Include(p => p.Aliases)
                .FirstOrDefaultAsync(p => p.Id == id);

            return entity == null ? null : _mapper.Map<PaymentLocationDto>(entity);
        }

        public async Task<PaymentLocationDto?> FindByNameOrAliasAsync(
            string name,
            bool activeOnly = true)
        {
            var normalizedName = PaymentLocationNameNormalizer.Normalize(name);
            if (string.IsNullOrEmpty(normalizedName))
                return null;

            var query = _context.PaymentLocations
                .AsNoTracking()
                .Include(p => p.Aliases)
                .Where(p => p.NormalizedName == normalizedName ||
                            p.Aliases.Any(a => a.NormalizedName == normalizedName));

            if (activeOnly)
                query = query.Where(p => p.IsActive);

            var entity = await query.FirstOrDefaultAsync();
            return entity == null ? null : _mapper.Map<PaymentLocationDto>(entity);
        }

        public async Task<PaymentLocationDto> CreateAsync(CreatePaymentLocationDto dto)
        {
            var entity = _mapper.Map<PaymentLocation>(dto);
            entity.Name = CleanDisplayName(dto.Name);
            entity.NormalizedName = PaymentLocationNameNormalizer.Normalize(entity.Name);
            entity.Address = dto.Address?.Trim() ?? string.Empty;
            entity.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            entity.ContactPerson = string.IsNullOrWhiteSpace(dto.ContactPerson) ? null : dto.ContactPerson.Trim();
            entity.CreatedAt = DateTime.UtcNow;
            entity.CreatedBy = GetCurrentUserId();

            await EnsureUniqueNamesAsync(entity.NormalizedName, dto.Aliases);
            entity.Aliases = BuildAliases(dto.Aliases, entity);

            await _context.PaymentLocations.AddAsync(entity);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "CREATE",
                "PaymentLocations",
                entity.Id,
                null,
                $"محل پرداخت '{entity.Name}' ایجاد شد",
                GetCurrentUserId()
            );

            return _mapper.Map<PaymentLocationDto>(entity);
        }

        public async Task<PaymentLocationDto> UpdateAsync(long id, UpdatePaymentLocationDto dto)
        {
            var entity = await _context.PaymentLocations
                .Include(p => p.Aliases)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (entity == null)
                throw new KeyNotFoundException($"محل پرداخت با شناسه {id} یافت نشد.");

            var cleanName = CleanDisplayName(dto.Name);
            var normalizedName = PaymentLocationNameNormalizer.Normalize(cleanName);
            await EnsureUniqueNamesAsync(normalizedName, dto.Aliases, id);

            entity.Name = cleanName;
            entity.NormalizedName = normalizedName;
            entity.Address = dto.Address?.Trim() ?? string.Empty;
            entity.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            entity.ContactPerson = string.IsNullOrWhiteSpace(dto.ContactPerson) ? null : dto.ContactPerson.Trim();
            entity.IsActive = dto.IsActive;
            _context.PaymentLocationAliases.RemoveRange(entity.Aliases);
            entity.Aliases = BuildAliases(dto.Aliases, entity);
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "UPDATE",
                "PaymentLocations",
                entity.Id,
                null,
                $"محل پرداخت '{entity.Name}' ویرایش شد",
                GetCurrentUserId()
            );

            return _mapper.Map<PaymentLocationDto>(entity);
        }

        public async Task DeleteAsync(long id)
        {
            var entity = await _context.PaymentLocations.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"محل پرداخت با شناسه {id} یافت نشد.");

            // بررسی اینکه آیا این آدرس در حواله‌ها استفاده شده است
            var isUsed = await _context.Hawalas.AnyAsync(h => h.PaymentLocationId == id);
            if (isUsed)
                throw new InvalidOperationException("این محل در حواله‌ها استفاده شده است و قابل حذف نیست.");

            _context.PaymentLocations.Remove(entity);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "DELETE",
                "PaymentLocations",
                id,
                null,
                $"محل پرداخت '{entity.Name}' حذف شد",
                GetCurrentUserId()
            );
        }

        public async Task<PaymentLocationDto> ToggleActiveAsync(long id)
        {
            var entity = await _context.PaymentLocations.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"محل پرداخت با شناسه {id} یافت نشد.");

            entity.IsActive = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "UPDATE",
                "PaymentLocations",
                entity.Id,
                null,
                $"وضعیت محل پرداخت '{entity.Name}' به {(entity.IsActive ? "فعال" : "غیرفعال")} تغییر یافت",
                GetCurrentUserId()
            );

            return _mapper.Map<PaymentLocationDto>(entity);
        }

        private long GetCurrentUserId() => _context.RequireCurrentUserId();

        private async Task EnsureUniqueNamesAsync(
            string normalizedName,
            IEnumerable<string>? aliases,
            long? excludedLocationId = null)
        {
            if (string.IsNullOrEmpty(normalizedName))
                throw new InvalidOperationException("نام محل پرداخت الزامی است.");

            var normalizedAliases = NormalizeAliases(aliases);
            if (normalizedAliases.Contains(normalizedName))
                throw new InvalidOperationException("نام اصلی محل نباید دوباره به‌عنوان نام جایگزین ثبت شود.");

            var requestedNames = normalizedAliases.Append(normalizedName).ToList();
            var duplicateExists = await _context.PaymentLocations
                .AsNoTracking()
                .Where(p => !excludedLocationId.HasValue || p.Id != excludedLocationId.Value)
                .AnyAsync(p => requestedNames.Contains(p.NormalizedName) ||
                               p.Aliases.Any(a => requestedNames.Contains(a.NormalizedName)));

            if (duplicateExists)
                throw new InvalidOperationException("نام یا نام جایگزین محل پرداخت قبلاً ثبت شده است.");
        }

        private List<PaymentLocationAlias> BuildAliases(
            IEnumerable<string>? aliases,
            PaymentLocation location)
        {
            var currentUserId = GetCurrentUserId();
            return (aliases ?? [])
                .Select(CleanDisplayName)
                .Where(x => !string.IsNullOrEmpty(x))
                .GroupBy(PaymentLocationNameNormalizer.Normalize)
                .Select(group => new PaymentLocationAlias
                {
                    TenantId = location.TenantId,
                    Name = group.First(),
                    NormalizedName = group.Key,
                    PaymentLocation = location,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = currentUserId
                })
                .ToList();
        }

        private static HashSet<string> NormalizeAliases(IEnumerable<string>? aliases) =>
            (aliases ?? [])
                .Select(PaymentLocationNameNormalizer.Normalize)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToHashSet(StringComparer.Ordinal);

        private static string CleanDisplayName(string? value) =>
            string.Join(' ', (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
