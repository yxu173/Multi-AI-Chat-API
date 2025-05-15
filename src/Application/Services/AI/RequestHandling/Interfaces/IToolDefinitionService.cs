using Domain.Enums;

namespace Application.Services.AI.RequestHandling.Interfaces;

public interface IToolDefinitionService
{
    Task<List<object>?> GetToolDefinitionsAsync(
        Guid userId,
        ModelType modelType,
        CancellationToken cancellationToken = default);
}
