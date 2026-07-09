using System;

namespace HawalaExchange.Application.DTOs
{
    public class UserDto
    {
        public long Id { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public long? BranchId { get; set; }
        public string? BranchName { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    public class CreateUserDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public long? BranchId { get; set; }
        public string Role { get; set; } = "Cashier";
    }

    public class UpdateUserDto
    {
        public string? FullName { get; set; }
        public long? BranchId { get; set; }
        public bool? IsActive { get; set; }
        public string? Role { get; set; }
    }

    public class UserFilterDto
    {
        public string? SearchTerm { get; set; }
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
        public long? BranchId { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SortColumn { get; set; } = "CreatedAt";
        public string SortDirection { get; set; } = "desc";
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class ResetPasswordDto
    {
        public long UserId { get; set; }
        public string NewPassword { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public class RegisterDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public long? BranchId { get; set; }
        public string Role { get; set; } = "Cashier";
    }

    public class LoginResponseDto
    {
        public long UserId { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
        public long? BranchId { get; set; }
        public string? Token { get; set; }
    }

    public class PaginatedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }
}