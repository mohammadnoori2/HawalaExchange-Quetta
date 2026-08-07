using HawalaExchange.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(
            RoleManager<IdentityRole<long>> roleManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context) // ✅ اضافه کردن context
        {
            // ==========================================
            // 1. ایجاد نقش‌ها
            // ==========================================
            string[] roleNames = { "SuperAdmin", "Admin", "Manager", "Cashier", "Supervisor" };

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
            if (adminUser == null)
            {
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

                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to create admin user: {errors}");
                }
            }

            foreach (var role in new[] { "Admin", "SuperAdmin" })
            {
                if (!await userManager.IsInRoleAsync(adminUser, role))
                {
                    var result = await userManager.AddToRoleAsync(adminUser, role);
                    if (!result.Succeeded)
                        throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}
