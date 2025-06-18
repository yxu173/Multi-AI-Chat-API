namespace Application.Abstractions.Interfaces;

public interface ISubscriptionService
{
    Task<(bool HasQuota, string? ErrorMessage)> CheckUserQuotaAsync(
        Guid userId,
        double requestCost,
        int requiredTokens = 0,
        CancellationToken cancellationToken = default);
        
    Task ResetMonthlyUsageAsync(CancellationToken cancellationToken = default);
} 