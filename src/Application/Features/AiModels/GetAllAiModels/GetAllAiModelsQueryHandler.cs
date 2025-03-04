using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.GetAllAiModels;

public sealed class GetAllAiModelsQueryHandler : IQueryHandler<GetAllAiModelsQuery, IReadOnlyList<AiModelDto>>
{
    private readonly IAiModelRepository _modelRepository;

    public GetAllAiModelsQueryHandler(IAiModelRepository modelRepository)
    {
        _modelRepository = modelRepository;
    }

    public async Task<Result<IReadOnlyList<AiModelDto>>> Handle(GetAllAiModelsQuery request,
        CancellationToken cancellationToken)
    {
        var aiModels = await _modelRepository.GetAllAsync();

        var dto = aiModels.Select(a => new AiModelDto(a.Id, a.Name, a.IsEnabled)).ToList();

        return Result.Success<IReadOnlyList<AiModelDto>>(dto);
    }
}