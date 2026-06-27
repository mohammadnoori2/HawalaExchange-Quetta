using AutoMapper;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HawalaExchange.Application.Services
{
    public abstract class BaseService<TEntity, TDto, TCreateDto, TUpdateDto>
        : IBaseService<TEntity, TDto, TCreateDto, TUpdateDto>
        where TEntity : class
        where TDto : class
        where TCreateDto : class
        where TUpdateDto : class
    {
        protected readonly ApplicationDbContext _context;
        protected readonly DbSet<TEntity> _dbSet;
        protected readonly IMapper _mapper;

        protected BaseService(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _dbSet = context.Set<TEntity>();
            _mapper = mapper;
        }

        public virtual async Task<TDto?> GetByIdAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            return entity == null ? null : _mapper.Map<TDto>(entity);
        }

        public virtual async Task<IEnumerable<TDto>> GetAllAsync()
        {
            var entities = await _dbSet.ToListAsync();
            return _mapper.Map<IEnumerable<TDto>>(entities);
        }

        public virtual async Task<IEnumerable<TDto>> FindAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var entities = await _dbSet.Where(predicate).ToListAsync();
            return _mapper.Map<IEnumerable<TDto>>(entities);
        }

        public virtual async Task<TDto> CreateAsync(TCreateDto createDto)
        {
            var entity = _mapper.Map<TEntity>(createDto);
            await ValidateCreateAsync(entity, createDto);

            await _dbSet.AddAsync(entity);
            await _context.SaveChangesAsync();

            return _mapper.Map<TDto>(entity);
        }

        public virtual async Task<TDto> UpdateAsync(long id, TUpdateDto updateDto)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"Entity with ID {id} not found.");

            _mapper.Map(updateDto, entity);
            await ValidateUpdateAsync(entity, updateDto);

            _dbSet.Update(entity);
            await _context.SaveChangesAsync();

            return _mapper.Map<TDto>(entity);
        }

        public virtual async Task DeleteAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"Entity with ID {id} not found.");

            await ValidateDeleteAsync(entity);

            _dbSet.Remove(entity);
            await _context.SaveChangesAsync();
        }

        public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await _dbSet.AnyAsync(predicate);
        }

        public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null)
        {
            if (predicate == null)
                return await _dbSet.CountAsync();
            return await _dbSet.CountAsync(predicate);
        }

        protected virtual Task ValidateCreateAsync(TEntity entity, TCreateDto dto) => Task.CompletedTask;
        protected virtual Task ValidateUpdateAsync(TEntity entity, TUpdateDto dto) => Task.CompletedTask;
        protected virtual Task ValidateDeleteAsync(TEntity entity) => Task.CompletedTask;
    }
}