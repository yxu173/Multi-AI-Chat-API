using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain.Aggregates.Admin;

namespace Domain.Repositories;

public interface IProviderApiKeyRepository
{
    Task<ProviderApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderApiKey>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderApiKey>> GetActiveByProviderIdAsync(Guid providerId, CancellationToken cancellationToken = default);
    Task<ProviderApiKey?> GetNextAvailableKeyAsync(Guid providerId, CancellationToken cancellationToken = default);
    Task<ProviderApiKey> AddAsync(ProviderApiKey providerApiKey, CancellationToken cancellationToken = default);
    Task<ProviderApiKey> UpdateAsync(ProviderApiKey providerApiKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ResetAllDailyUsageAsync(CancellationToken cancellationToken = default);
}
