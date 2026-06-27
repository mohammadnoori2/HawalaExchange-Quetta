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

        protected override async Task ValidateCreateAsync(Branch entity, CreateBranchDto dto)
        {
            if (await _dbSet.AnyAsync(b => b.Code == entity.Code))
                throw new InvalidOperationException($"Branch with code '{entity.Code}' already exists.");
        }
    }
}