using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class CorrespondentService : BaseService<Correspondent, CorrespondentDto, CreateCorrespondentDto, UpdateCorrespondentDto>, ICorrespondentService
    {
        public CorrespondentService(ApplicationDbContext context, IMapper mapper)
            : base(context, mapper) { }

        public async Task<CorrespondentDto?> GetByCodeAsync(string code)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(c => c.Code == code);
            return entity == null ? null : _mapper.Map<CorrespondentDto>(entity);
        }

        public async Task<IEnumerable<CorrespondentDto>> GetByCountryAsync(string country)
        {
            var entities = await _dbSet
                .Where(c => c.Country == country && !c.IsArchived)
                .ToListAsync();
            return _mapper.Map<IEnumerable<CorrespondentDto>>(entities);
        }

        public async Task<IEnumerable<CorrespondentDto>> GetActiveAsync()
        {
            var entities = await _dbSet.Where(c => !c.IsArchived).ToListAsync();
            return _mapper.Map<IEnumerable<CorrespondentDto>>(entities);
        }

        public async Task<CorrespondentDto> ArchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Correspondent with ID {id} not found.");

            entity.IsArchived = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<CorrespondentDto>(entity);
        }

        public async Task<CorrespondentDto> UnarchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Correspondent with ID {id} not found.");

            entity.IsArchived = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<CorrespondentDto>(entity);
        }

        protected override async Task ValidateCreateAsync(Correspondent entity, CreateCorrespondentDto dto)
        {
            if (await _dbSet.AnyAsync(c => c.Code == entity.Code))
                throw new InvalidOperationException($"Correspondent with code '{entity.Code}' already exists.");
        }
    }
}