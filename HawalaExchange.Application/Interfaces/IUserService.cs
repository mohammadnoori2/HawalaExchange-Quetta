using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IUserService : IBaseService<User, UserDto, CreateUserDto, UpdateUserDto>
    {
        Task<UserDto?> GetByUserNameAsync(string userName);
        Task<UserDto?> AuthenticateAsync(string userName, string password);
        Task<IEnumerable<UserDto>> GetUsersByBranchAsync(long branchId);
        Task ChangePasswordAsync(long userId, string currentPassword, string newPassword);
        Task ResetPasswordAsync(long userId, string newPassword);
    }
}