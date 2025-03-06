using Application.Abstractions.Messaging;
using Application.Features.AiModels.GetAllAiModels;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.GetEnabledAiModels;

public sealed class GetEnabledAiModelsQueryHandler : IQueryHandler<GetEnabledAiModelsQuery, IReadOnlyList<AiModelDto>>
{
    private readonly IAiModelRepository _aiModelRepository;

    public GetEnabledAiModelsQueryHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<IReadOnlyList<AiModelDto>>> Handle(GetEnabledAiModelsQuery request,
        CancellationToken cancellationToken)
    {
        var aiModels = await _aiModelRepository.GetEnabledAsync();

        var result = aiModels.Select(a => new AiModelDto(a.Id, a.Name, a.IsEnabled)).ToList();
        return Result.Success<IReadOnlyList<AiModelDto>>(result);
    }
}