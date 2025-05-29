namespace Infrastructure.Services.Subscription;

public interface ISubscriptionService
{
    Task<(bool HasQuota, string? ErrorMessage)> CheckUserQuotaAsync(
        Guid userId,
        int requiredTokens = 0,
        CancellationToken cancellationToken = default);


    Task ResetDailyUsageAsync(CancellationToken cancellationToken = default);
}