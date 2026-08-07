using HawalaExchange.Application.Interfaces.Services;

namespace HawalaExchange.Web.Services;

public sealed class SaasAutomationWorker(IServiceScopeFactory scopeFactory, ILogger<SaasAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
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
            await Task.Delay(delay, stoppingToken);
        }
    }
}
