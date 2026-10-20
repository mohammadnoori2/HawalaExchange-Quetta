using HawalaExchange.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HawalaExchange.Infrastructure.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(
            RoleManager<IdentityRole<long>> roleManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            bool isDevelopment,
            IConfiguration configuration)
        {
            // ==========================================
            // 1. ایجاد نقش‌ها
            // ==========================================
            string[] roleNames = ["SuperAdmin", "Admin", "Manager", "Cashier", "Supervisor", .. PlatformRoles.All];

            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole<long>
                    {
                        Name = roleName
                    });
                }
            }

            // ==========================================
            // 2. ایجاد صرافی و شعبه پیش‌فرض (اگر وجود ندارد)
            // ==========================================
            var tenant = await context.Tenants
                .IgnoreQueryFilters()
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();
            if (tenant == null)
            {
                tenant = new Tenant
                {
                    Name = "صرافی پیش‌فرض",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                context.Tenants.Add(tenant);
                await context.SaveChangesAsync();
            }

            using var tenantScope = context.UseTenantScope(tenant.Id);

            var branch = await context.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.TenantId == tenant.Id && b.Code == "MAIN");
            if (branch == null)
            {
                branch = new Branch
                {
                    TenantId = tenant.Id,
                    Code = "MAIN",
                    Name = "شعبه اصلی",
                    Address = "آدرس شعبه اصلی",
                    PhoneNumber = "021-12345678",
                    IsArchived = false,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Branches.AddAsync(branch);
                await context.SaveChangesAsync();
            }

            // ==========================================
            // 3. ایجاد کاربر ادمین
            // ==========================================
            var adminUser = await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x =>
                    x.TenantId == tenant.Id && x.LocalUserName == "admin");
            // Bootstrap only an empty installation. Never recreate an account or
            // restore privileges that an administrator intentionally removed.
            if (adminUser == null && !await context.Users.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant.Id && !x.IsPlatformUser))
            {
                var bootstrapPassword = configuration["BootstrapAdmin:Password"];
                if (string.IsNullOrWhiteSpace(bootstrapPassword) || bootstrapPassword.Length < 12)
                    throw new InvalidOperationException("برای نصب اولیه BootstrapAdmin:Password را با یک رمز قوی حداقل 12 حرفی در تنظیمات امن سرور تعیین کنید.");
                adminUser = new ApplicationUser
                {
                    TenantId = tenant.Id,
                    LocalUserName = "admin",
                    UserName = $"{tenant.Id}:admin",
                    Email = "admin@hawalaexchange.com",
                    FullName = "مدیر سیستم",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    BranchId = branch.Id
                };

                var result = await userManager.CreateAsync(adminUser, bootstrapPassword);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to create admin user: {errors}");
                }
                foreach (var role in new[] { "Admin", "SuperAdmin" })
                {
                    var roleResult = await userManager.AddToRoleAsync(adminUser, role);
                    if (!roleResult.Succeeded)
                        throw new Exception(string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }
            }

            var platformPassword = configuration["PlatformOwner:Password"];

            if (!string.IsNullOrWhiteSpace(platformPassword))
            {
                var platformUserName = configuration["PlatformOwner:UserName"] ?? "platformadmin";
                var platformUser = await context.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.IsPlatformUser && x.LocalUserName == platformUserName);
                if (platformUser is null)
                {
                    if (platformPassword.Length < 12)
                        throw new InvalidOperationException("رمز اولیه مالک پلتفرم باید حداقل 12 حرف داشته باشد.");
                    platformUser = new ApplicationUser
                    {
                        IsPlatformUser = true,
                        TenantId = tenant.Id,
                        LocalUserName = platformUserName,
                        UserName = $"platform:{platformUserName}",
                        Email = configuration["PlatformOwner:Email"] ?? "platform@hawalaexchange.local",
                        FullName = "مالک پلتفرم",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    var createPlatformUser = await userManager.CreateAsync(platformUser, platformPassword);
                    if (!createPlatformUser.Succeeded)
                        throw new Exception(string.Join(", ", createPlatformUser.Errors.Select(x => x.Description)));
                    var assignOwner = await userManager.AddToRoleAsync(platformUser, PlatformRoles.Owner);
                    if (!assignOwner.Succeeded)
                        throw new InvalidOperationException(string.Join("؛ ", assignOwner.Errors.Select(x => x.Description)));
                }
            }
        }
    }
}
