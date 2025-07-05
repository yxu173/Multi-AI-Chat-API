using Application.Abstractions.Interfaces;
using Domain.Enums;

namespace Application.Services.AI.RequestHandling.Interfaces;

public interface IToolDefinitionService
{
    Task<List<PluginDefinition>?> GetToolDefinitionsAsync(
        Guid userId,
        bool enableDeepSearch,
        ModelType? modelType = null,
        CancellationToken cancellationToken = default);
}
