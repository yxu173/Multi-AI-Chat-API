using Application.Abstractions.Interfaces;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using System; // Added for ArgumentNullException, TimeSpan
using System.Threading;
using System.Threading.Tasks;
using Domain.Aggregates.Admin;

namespace Infrastructure.Services.Subscription;

public class ProviderKeyManagementService : IProviderKeyManagementService
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;
    private readonly IAiProviderRepository _aiProviderRepository;
    private readonly ILogger<ProviderKeyManagementService> _logger;

    public ProviderKeyManagementService(
        IProviderApiKeyRepository providerApiKeyRepository,
        IAiProviderRepository aiProviderRepository,
        ILogger<ProviderKeyManagementService> logger)
    {
        _providerApiKeyRepository = providerApiKeyRepository ?? throw new ArgumentNullException(nameof(providerApiKeyRepository));
        _aiProviderRepository = aiProviderRepository ?? throw new ArgumentNullException(nameof(aiProviderRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProviderApiKey?> GetProviderApiKeyObjectAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _providerApiKeyRepository.GetNextAvailableKeyAsync(providerId, cancellationToken);
        
        if (apiKey != null)
        {
            return apiKey;
        }

        _logger.LogWarning("No managed API key available for provider {ProviderId}. Attempting to use default key.", providerId);
        return null; 
    }

    public async Task<string?> GetAvailableApiKeyAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _providerApiKeyRepository.GetNextAvailableKeyAsync(providerId, cancellationToken);
        
        if (apiKey != null)
        {
            return apiKey.Secret;
        }

        var provider = await _aiProviderRepository.GetByIdAsync(providerId);
        if (provider != null && !string.IsNullOrEmpty(provider.DefaultApiKey))
        {
            _logger.LogWarning(
                "No API keys with available quota found for provider {ProviderId}. Using default key.", 
                providerId);
            return provider.DefaultApiKey;
        }

        _logger.LogError("No API keys available for provider {ProviderId}, including default.", providerId);
        return null;
    }

    public async Task ReportKeySuccessAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _providerApiKeyRepository.IncrementUsageAsync(apiKeyId, cancellationToken);
            _logger.LogDebug("Successfully incremented usage for API key {ApiKeyId}", apiKeyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment usage for API key {ApiKeyId}", apiKeyId);
        }
    }

    public async Task ReportKeyRateLimitedAsync(Guid apiKeyId, TimeSpan retryAfter, CancellationToken cancellationToken = default)
    {
        try
        {
            var rateLimitedUntil = DateTime.UtcNow.Add(retryAfter);
            await _providerApiKeyRepository.MarkAsRateLimitedAsync(apiKeyId, rateLimitedUntil, cancellationToken);
            _logger.LogWarning("API key {ApiKeyId} marked as rate-limited until {RateLimitedUntil}", apiKeyId, rateLimitedUntil);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark API key {ApiKeyId} as rate-limited", apiKeyId);
        }
    }

    public async Task ClearExpiredRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _providerApiKeyRepository.ClearExpiredRateLimitsAsync(cancellationToken);
            _logger.LogInformation("Successfully cleared expired rate limits for provider API keys.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear expired rate limits for provider API keys.");
            throw; // Re-throw as this is a background task, failure should be noticeable
        }
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
