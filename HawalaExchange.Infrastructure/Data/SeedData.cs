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
            string[] roleNames = { "Admin", "Manager", "Cashier", "Supervisor" };

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
            // 2. ایجاد شعبه پیش‌فرض (اگر وجود ندارد)
            // ==========================================
            var branch = await context.Branches.FirstOrDefaultAsync(b => b.Code == "MAIN");
            if (branch == null)
            {
                branch = new Branch
                {
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
            var adminUser = await userManager.FindByNameAsync("admin");
            if (adminUser == null)
            {
                var user = new ApplicationUser
                {
                    UserName = "admin",
                    Email = "admin@hawalaexchange.com",
                    FullName = "مدیر سیستم",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    BranchId = branch.Id // ✅ استفاده از Branch ایجاد شده
                };

                var result = await userManager.CreateAsync(user, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "Admin");
                }
                else
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to create admin user: {errors}");
                }
            }
        }
    }
}