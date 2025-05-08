using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.Queries.GetAllAiModels;

public sealed class GetAllAiModelsQueryHandler : IQueryHandler<GetAllAiModelsQuery, IReadOnlyList<AiModelDto>>
{
    private readonly IAiModelRepository _modelRepository;

    public GetAllAiModelsQueryHandler(IAiModelRepository modelRepository)
    {
        _modelRepository = modelRepository;
    }

    public async Task<Result<IReadOnlyList<AiModelDto>>> ExecuteAsync(GetAllAiModelsQuery command, CancellationToken ct)
    {
        var aiModels = await _modelRepository.GetAllAsync();

        var dto = aiModels.Select(a => new AiModelDto(
            a.Id,
            a.Name,
            a.IsEnabled,
            a.SupportsVision,
            a.ContextLength,
            a.PluginsSupported
        )).ToList();

        return Result.Success<IReadOnlyList<AiModelDto>>(dto);
    }
}