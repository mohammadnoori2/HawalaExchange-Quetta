using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Web.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DatabaseInitializer));

        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();

            logger.LogInformation("Applying pending database migrations.");
            await context.Database.MigrateAsync(cancellationToken);
            await context.EnsureApplicationSchemaAsync(cancellationToken);

            logger.LogInformation("Seeding required application data.");
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole<long>>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            await SeedData.InitializeAsync(
                roleManager,
                userManager,
                context,
                app.Environment.IsDevelopment(),
                app.Configuration);

            var tenantIds = await context.Tenants
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var currencyCostService = services.GetRequiredService<ICurrencyCostService>();
            foreach (var tenantId in tenantIds)
            {
                using var tenantScope = context.UseTenantScope(tenantId);
                using var subscriptionBypass = context.BypassSubscriptionEnforcement();
                await context.EnsureSystemAccountsAsync(tenantId, cancellationToken);
                await currencyCostService.RebuildAsync(cancellationToken);
            }

            logger.LogInformation("Database initialization completed successfully.");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Database initialization failed. Application startup was stopped.");
            throw;
        }
    }
}
