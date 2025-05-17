using System;
using System.Threading.Tasks;
using Domain.Enums;
using Domain.Aggregates.Admin;

namespace Application.Abstractions.Interfaces;

public class AiServiceContext
{
    public IAiModelService Service { get; }
    public ProviderApiKey ApiKey { get; }
    public string? DefaultApiKeyFallback { get; }

    public AiServiceContext(IAiModelService service, ProviderApiKey apiKey)
    {
        Service = service;
        ApiKey = apiKey;
        DefaultApiKeyFallback = null;
    }
    public AiServiceContext(IAiModelService service, string defaultApiKeyFallback)
    {
        Service = service;
        ApiKey = null;
        DefaultApiKeyFallback = defaultApiKeyFallback;
    }
}

public interface IAiModelServiceFactory
{
    [Obsolete("Use GetServiceContextAsync instead for better error handling and API key management.")]
    IAiModelService GetService(Guid userId, Guid modelId, Guid? aiAgentId = null);

    Task<AiServiceContext?> GetServiceContextAsync(
        Guid userId, 
        Guid modelId, 
        Guid? aiAgentId = null, 
        CancellationToken cancellationToken = default);
}