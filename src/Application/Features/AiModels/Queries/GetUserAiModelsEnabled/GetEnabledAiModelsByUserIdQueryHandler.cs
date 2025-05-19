using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiModels.Queries.GetUserAiModelsEnabled;

public sealed class
    GetEnabledAiModelsByUserIdQueryHandler : IQueryHandler<GetEnabledAiModelsByUserIdQuery, IReadOnlyList<AiModelDto>>
{
    private readonly IAiModelRepository _aiModelRepository;

    public GetEnabledAiModelsByUserIdQueryHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<IReadOnlyList<AiModelDto>>> ExecuteAsync(GetEnabledAiModelsByUserIdQuery request, CancellationToken ct)
    {
        var aiModels = await _aiModelRepository.GetEnabledByUserIdAsync(request.UserId);

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