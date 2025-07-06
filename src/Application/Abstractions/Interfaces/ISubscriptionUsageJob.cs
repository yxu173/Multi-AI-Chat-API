namespace Application.Abstractions.Interfaces;

public interface ISubscriptionUsageJob
{
    Task IncrementUsageAsync(Guid userId, double requestCost);
} 