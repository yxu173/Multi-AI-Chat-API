namespace Infrastructure.Services.Subscription;

public interface IProviderKeyManagementService
{
    Task<string?> GetAvailableApiKeyAsync(Guid providerId, CancellationToken cancellationToken = default);


    Task ResetDailyUsageAsync(CancellationToken cancellationToken = default);
}