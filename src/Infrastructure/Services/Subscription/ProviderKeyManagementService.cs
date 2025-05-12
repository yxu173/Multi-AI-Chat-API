using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Subscription;

public class ProviderKeyManagementService : IProviderKeyManagementService
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;
    private readonly IAiProviderRepository _aiProviderRepository;
    private readonly ILogger<ProviderKeyManagementService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ProviderKeyManagementService(
        IProviderApiKeyRepository providerApiKeyRepository,
        IAiProviderRepository aiProviderRepository,
        ILogger<ProviderKeyManagementService> logger,
        IServiceProvider serviceProvider)
    {
        _providerApiKeyRepository = providerApiKeyRepository ?? throw new ArgumentNullException(nameof(providerApiKeyRepository));
        _aiProviderRepository = aiProviderRepository ?? throw new ArgumentNullException(nameof(aiProviderRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<string?> GetAvailableApiKeyAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        // First, try to get an API key with available quota
        var apiKey = await _providerApiKeyRepository.GetNextAvailableKeyAsync(providerId, cancellationToken);
        
        if (apiKey != null)
        {
            var keyId = apiKey.Id;
            var aiProviderId = apiKey.AiProviderId;
            
            _ = Task.Run(async () => 
            {
                using var scope = _serviceProvider.CreateScope();
                var providerKeyRepo = scope.ServiceProvider.GetRequiredService<IProviderApiKeyRepository>();
                
                try
                {
                    var freshApiKey = await providerKeyRepo.GetByIdAsync(keyId, CancellationToken.None);
                    if (freshApiKey != null)
                    {
                        freshApiKey.UpdateUsage();
                        await providerKeyRepo.UpdateAsync(freshApiKey, CancellationToken.None);
                        _logger.LogDebug("Updated usage for API key {KeyId} (Provider: {ProviderId})", keyId, aiProviderId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to increment usage for API key {KeyId}", keyId);
                }
            });
            
            return apiKey.Secret;
        }

        // If no API key with quota is available, try to get the provider's default key as fallback
        var provider = await _aiProviderRepository.GetByIdAsync(providerId);
        if (provider != null && !string.IsNullOrEmpty(provider.DefaultApiKey))
        {
            _logger.LogWarning(
                "No API keys with available quota found for provider {ProviderId}. Using default key.", 
                providerId);
            return provider.DefaultApiKey;
        }

        _logger.LogError("No API keys available for provider {ProviderId}", providerId);
        return null;
    }

    public async Task ResetDailyUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _providerApiKeyRepository.ResetAllDailyUsageAsync(cancellationToken);
            _logger.LogInformation("Successfully reset daily usage for all provider API keys");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset daily usage for provider API keys");
            throw;
        }
    }
}
