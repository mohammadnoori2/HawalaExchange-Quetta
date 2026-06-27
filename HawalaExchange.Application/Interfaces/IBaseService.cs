using System.Linq.Expressions;

namespace HawalaExchange.Application.Interfaces.Services
{
    /// <summary>
    /// Base service interface with common CRUD operations
    /// </summary>
    public interface IBaseService<TEntity, TDto, TCreateDto, TUpdateDto>
        where TEntity : class
        where TDto : class
        where TCreateDto : class
        where TUpdateDto : class
    {
        Task<TDto?> GetByIdAsync(long id);
        Task<IEnumerable<TDto>> GetAllAsync();
        Task<IEnumerable<TDto>> FindAsync(Expression<Func<TEntity, bool>> predicate);
        Task<TDto> CreateAsync(TCreateDto createDto);
        Task<TDto> UpdateAsync(long id, TUpdateDto updateDto);
        Task DeleteAsync(long id);
        Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);
        Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null);
    }
}