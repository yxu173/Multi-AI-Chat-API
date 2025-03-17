using Application.Abstractions.Messaging;
using Application.Features.AiModels.GetAllAiModels;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.GetUserAiModels;

public sealed class GetUserAiModelsQueryHandler : IQueryHandler<GetUserAiModelsQuery, IReadOnlyList<AiModelDto>>
{
    private readonly IAiModelRepository _aiModelRepository;

    public GetUserAiModelsQueryHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<IReadOnlyList<AiModelDto>>> Handle(GetUserAiModelsQuery request,
        CancellationToken cancellationToken)
    {
        var aiModels = await _aiModelRepository.GetUserAiModelsAsync(request.UserId);

        var result = aiModels.Select(a => new AiModelDto(a.Id, a.Name, a.IsEnabled)).ToList();
        return Result.Success<IReadOnlyList<AiModelDto>>(result);
    }
}