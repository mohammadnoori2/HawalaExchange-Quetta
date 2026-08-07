using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class BranchService : BaseService<Branch, BranchDto, CreateBranchDto, UpdateBranchDto>, IBranchService
    {
        public BranchService(ApplicationDbContext context, IMapper mapper)
            : base(context, mapper) { }

        public async Task<BranchDto?> GetByCodeAsync(string code)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(b => b.Code == code);
            return entity == null ? null : _mapper.Map<BranchDto>(entity);
        }

        public async Task<IEnumerable<BranchDto>> GetActiveBranchesAsync()
        {
            var entities = await _dbSet.Where(b => !b.IsArchived).ToListAsync();
            return _mapper.Map<IEnumerable<BranchDto>>(entities);
        }

        public override async Task<IEnumerable<BranchDto>> GetAllAsync()
        {
            var entities = await _dbSet.AsNoTracking().ToListAsync();
            var branchIds = entities.Select(b => b.Id).ToList();

            var transactionBranchIds = await _context.Transactions
                .Where(t => branchIds.Contains(t.BranchId))
                .Select(t => t.BranchId)
                .Distinct()
                .ToListAsync();

            var userBranchIds = await _context.Users
                .Where(u => branchIds.Contains(u.BranchId))
                .Select(u => u.BranchId)
                .Distinct()
                .ToListAsync();

            var blockedBranchIds = transactionBranchIds
                .Concat(userBranchIds)
                .ToHashSet();

            return entities.Select(entity =>
            {
                var dto = _mapper.Map<BranchDto>(entity);
                dto.CanDelete = !blockedBranchIds.Contains(entity.Id);
                return dto;
            }).ToList();
        }

        protected override async Task ValidateCreateAsync(Branch entity, CreateBranchDto dto)
        {
            if (await _dbSet.AnyAsync(b => b.Code == entity.Code))
                throw new InvalidOperationException($"Branch with code '{entity.Code}' already exists.");
        }
        public async Task<BranchDto> ArchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"Branch with ID {id} not found.");

            entity.IsArchived = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<BranchDto>(entity);
        }

        public async Task<BranchDto> UnarchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"Branch with ID {id} not found.");

            entity.IsArchived = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<BranchDto>(entity);
        }

        protected override async Task ValidateDeleteAsync(Branch entity)
        {
            if (await _context.Transactions.AnyAsync(t => t.BranchId == entity.Id))
                throw new InvalidOperationException(
                    "این نمایندگی دارای معامله، حواله یا تراکنش ثبت‌شده است و قابل حذف نیست؛ می‌توانید آن را بایگانی کنید.");

            if (await _context.Users.AnyAsync(u => u.BranchId == entity.Id))
                throw new InvalidOperationException(
                    "این نمایندگی دارای کاربر وابسته است. ابتدا کاربر را به نمایندگی دیگری انتقال دهید.");

        }
    }
}
