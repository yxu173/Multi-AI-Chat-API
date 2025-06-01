using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Subscription;

public class DailyQuotaResetService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailyQuotaResetService> _logger;

    public DailyQuotaResetService(
        IServiceProvider serviceProvider,
        ILogger<DailyQuotaResetService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Daily Quota Reset Service is starting.");

        var now = DateTime.UtcNow;
        var nextUtcMidnight = now.Date.AddDays(1);
        var initialDelay = nextUtcMidnight - now;

        _logger.LogInformation("Next reset scheduled for {NextReset} (UTC). Initial delay: {Delay}",
            nextUtcMidnight, initialDelay);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait until next UTC midnight
                await Task.Delay(initialDelay, stoppingToken);

                // Reset all quotas
                using (var scope = _serviceProvider.CreateScope())
                {
                    var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
                    var keyManagementService = scope.ServiceProvider.GetRequiredService<IProviderKeyManagementService>();

                    _logger.LogInformation("Attempting to reset daily usage for user subscriptions and API keys...");
                    await subscriptionService.ResetMonthlyUsageAsync(stoppingToken);
                    await keyManagementService.ResetDailyUsageAsync(stoppingToken);
                    _logger.LogInformation("Successfully reset all daily usage counters.");

                    _logger.LogInformation("Attempting to clear expired rate limits for API keys...");
                    await keyManagementService.ClearExpiredRateLimitsAsync(stoppingToken);
                    _logger.LogInformation("Successfully cleared expired rate limits.");
                }

                // Calculate delay for the next day
                initialDelay = TimeSpan.FromDays(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while resetting daily quotas");
                
                // Wait for 15 minutes before retrying in case of failure
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Daily Quota Reset Service is stopping.");
    }
}
