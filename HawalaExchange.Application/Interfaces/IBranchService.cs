using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IBranchService : IBaseService<Branch, BranchDto, CreateBranchDto, UpdateBranchDto>
    {
        Task<BranchDto?> GetByCodeAsync(string code);
        Task<IEnumerable<BranchDto>> GetActiveBranchesAsync();
    }
}