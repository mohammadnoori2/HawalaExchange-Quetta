using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.Web.Services;

public sealed class HawalaImportWorker(IServiceScopeFactory scopes, ILogger<HawalaImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(5);
            try
            {
                // This is the only cross-tenant read: trusted queue metadata, never client IDs.
                using var discovery = scopes.CreateScope();
                var adminContext = discovery.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var next = await adminContext.HawalaImportJobs.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.Status == "Running" || x.Status == "Queued")
                    .OrderBy(x => x.Status == "Running" ? 0 : 1).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
                    .Select(x => new { x.Id, x.TenantId, x.RequestedBy }).FirstOrDefaultAsync(stoppingToken);
                if (next is not null)
                {
                    using var execution = scopes.CreateScope();
                    var identity = execution.ServiceProvider.GetRequiredService<CurrentTenant>();
                    identity.SetTenant(next.TenantId);
                    identity.SetUser(next.RequestedBy);
                    var queue = execution.ServiceProvider.GetRequiredService<HawalaImportQueue>();
                    if (await queue.ProcessAsync(next.Id, stoppingToken)) delay = TimeSpan.FromMilliseconds(250);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError(error, "Background hawala import cycle failed; durable requests remain in the database.");
                delay = TimeSpan.FromSeconds(15);
            }
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
