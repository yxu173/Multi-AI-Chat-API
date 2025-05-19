using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Admin.ProviderApiKeys.GetProviderApiKeys;

internal sealed class GetProviderApiKeysQueryHandler : IQueryHandler<GetProviderApiKeysQuery, IReadOnlyList<ProviderApiKey>>
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;

    public GetProviderApiKeysQueryHandler(IProviderApiKeyRepository providerApiKeyRepository)
    {
        _providerApiKeyRepository = providerApiKeyRepository;
    }

    public async Task<Result<IReadOnlyList<ProviderApiKey>>> ExecuteAsync(GetProviderApiKeysQuery query, CancellationToken ct)
    {
        var apiKeys = await _providerApiKeyRepository.GetAllAsync(ct);

        if (query.ProviderId.HasValue)
        {
            apiKeys = apiKeys.Where(k => k.AiProviderId == query.ProviderId.Value).ToList();
        }

        return Result.Success(apiKeys);
    }
}
