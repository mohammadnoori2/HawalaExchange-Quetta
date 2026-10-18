using HawalaExchange.Application.Interfaces.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.Web.Services;

public sealed class SaasAutomationWorker(IServiceScopeFactory scopeFactory, ILogger<SaasAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await WaitAsync(TimeSpan.FromMinutes(2), stoppingToken)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromHours(6);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISaasAutomationService>();
                var dashboard = await service.GetDashboardAsync(cancellationToken: stoppingToken);
                delay = TimeSpan.FromMinutes(Math.Clamp(dashboard.Settings.RunIntervalMinutes, 5, 1440));
                if (dashboard.Settings.IsEnabled) await service.RunAsync(false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "The SaaS automation cycle failed."); }
            if (!await WaitAsync(delay, stoppingToken)) break;
        }
    }

    internal static async Task<bool> WaitAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try { await Task.Delay(delay, stoppingToken); return true; }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return false; }
    }
}
