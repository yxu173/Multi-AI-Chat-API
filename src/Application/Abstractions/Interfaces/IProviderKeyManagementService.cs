using System;
using System.Threading;
using System.Threading.Tasks;
using Domain.Aggregates.Admin;

namespace Application.Abstractions.Interfaces;

public interface IProviderKeyManagementService
{
    Task<string?> GetAvailableApiKeyAsync(Guid providerId, CancellationToken cancellationToken = default);
    Task<ProviderApiKey?> GetProviderApiKeyObjectAsync(Guid providerId, CancellationToken cancellationToken = default);

    Task ReportKeySuccessAsync(Guid apiKeyId, CancellationToken cancellationToken = default);
    Task ReportKeyRateLimitedAsync(Guid apiKeyId, TimeSpan retryAfter, CancellationToken cancellationToken = default);
    
    Task ResetDailyUsageAsync(CancellationToken cancellationToken = default);
    Task ClearExpiredRateLimitsAsync(CancellationToken cancellationToken = default);
} 