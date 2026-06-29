using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace HawalaExchange.Application.Services
{
    public class UserService : BaseService<User, UserDto, CreateUserDto, UpdateUserDto>, IUserService
    {
        public UserService(ApplicationDbContext context, IMapper mapper)
            : base(context, mapper) { }

        public async Task<UserDto?> GetByUserNameAsync(string userName)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(u => u.UserName == userName);
            return entity == null ? null : _mapper.Map<UserDto>(entity);
        }

        public async Task<UserDto?> AuthenticateAsync(string userName, string password)
        {
            var user = await _dbSet.FirstOrDefaultAsync(u => u.UserName == userName && u.IsActive);
            if (user == null) return null;

            var passwordHash = HashPassword(password);
            if (user.PasswordHash != passwordHash) return null;

            return _mapper.Map<UserDto>(user);
        }

        public async Task<IEnumerable<UserDto>> GetUsersByBranchAsync(long branchId)
        {
            var users = await _dbSet.Where(u => u.BranchId == branchId).ToListAsync();
            return _mapper.Map<IEnumerable<UserDto>>(users);
        }

        public async Task ChangePasswordAsync(long userId, string currentPassword, string newPassword)
        {
            var user = await _dbSet.FindAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found.");

            if (user.PasswordHash != HashPassword(currentPassword))
                throw new InvalidOperationException("Current password is incorrect.");

            user.PasswordHash = HashPassword(newPassword);
            await _context.SaveChangesAsync();
        }

        public async Task ResetPasswordAsync(long userId, string newPassword)
        {
            var user = await _dbSet.FindAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found.");

            user.PasswordHash = HashPassword(newPassword);
            await _context.SaveChangesAsync();
        }

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        protected override async Task ValidateCreateAsync(User entity, CreateUserDto dto)
        {
            if (await _dbSet.AnyAsync(u => u.UserName == entity.UserName))
                throw new InvalidOperationException($"Username '{entity.UserName}' is already taken.");

            entity.PasswordHash = HashPassword(dto.Password);
        }
        public async Task<UserDto> ActivateAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"User with ID {id} not found.");
            entity.IsActive = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<UserDto>(entity);
        }

        public async Task<UserDto> DeactivateAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"User with ID {id} not found.");
            entity.IsActive = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<UserDto>(entity);
        }
    }
}