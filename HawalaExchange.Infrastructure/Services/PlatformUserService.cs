using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class PlatformUserService(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager) : IPlatformUserService
{
    public async Task<IReadOnlyList<PlatformUserDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsPlatformUser)
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);
        var result = new List<PlatformUserDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add(Map(user, roles.FirstOrDefault(x => PlatformRoles.All.Contains(x)) ?? string.Empty));
        }
        return result;
    }

    public async Task<PlatformUserDto> CreateAsync(CreatePlatformUserDto dto, CancellationToken cancellationToken = default)
    {
        if (!PlatformRoles.All.Contains(dto.Role))
            throw new InvalidOperationException("نقش پلتفرم معتبر نیست.");
        var localName = dto.UserName.Trim();
        if (await context.Users.IgnoreQueryFilters().AnyAsync(x => x.LocalUserName == localName, cancellationToken))
            throw new InvalidOperationException("این نام کاربری قبلاً ثبت شده است.");

        var user = new ApplicationUser
        {
            IsPlatformUser = true,
            TenantId = await context.Tenants.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(cancellationToken),
            BranchId = null,
            LocalUserName = localName,
            UserName = $"platform:{localName}",
            FullName = dto.FullName.Trim(),
            Email = dto.Email.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var create = await userManager.CreateAsync(user, dto.Password);
        if (!create.Succeeded)
            throw new InvalidOperationException(string.Join("؛ ", create.Errors.Select(x => x.Description)));
        var role = await userManager.AddToRoleAsync(user, dto.Role);
        if (!role.Succeeded)
            throw new InvalidOperationException(string.Join("؛ ", role.Errors.Select(x => x.Description)));

        context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            Action = "CREATE_PLATFORM_USER",
            EntityName = nameof(ApplicationUser),
            EntityId = user.Id,
            Details = $"Role={dto.Role}"
        });
        await context.SaveChangesAsync(cancellationToken);
        return Map(user, dto.Role);
    }

    public async Task SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await context.Users.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsPlatformUser, cancellationToken)
            ?? throw new KeyNotFoundException("کاربر پلتفرم یافت نشد.");
        user.IsActive = isActive;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("؛ ", result.Errors.Select(x => x.Description)));
    }

    private static PlatformUserDto Map(ApplicationUser user, string role) => new()
    {
        Id = user.Id,
        UserName = user.LocalUserName,
        FullName = user.FullName,
        Email = user.Email ?? string.Empty,
        Role = role,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt
    };
}
