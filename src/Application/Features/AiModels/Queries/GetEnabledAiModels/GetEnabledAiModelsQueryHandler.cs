using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiModels.Queries.GetEnabledAiModels;

public sealed class GetEnabledAiModelsQueryHandler : IQueryHandler<GetEnabledAiModelsQuery, IReadOnlyList<AiModelDto>>
{
    private readonly IAiModelRepository _aiModelRepository;

    public GetEnabledAiModelsQueryHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<IReadOnlyList<AiModelDto>>> ExecuteAsync(GetEnabledAiModelsQuery command,
        CancellationToken ct)
    {
        var aiModels = await _aiModelRepository.GetEnabledAsync();

        var result = aiModels.Select(a => new AiModelDto(
            a.Id,
            a.Name,
            a.IsEnabled,
            a.SupportsVision,
            a.ContextLength,
            a.PluginsSupported
        )).ToList();
        return Result.Success<IReadOnlyList<AiModelDto>>(result);
    }
}