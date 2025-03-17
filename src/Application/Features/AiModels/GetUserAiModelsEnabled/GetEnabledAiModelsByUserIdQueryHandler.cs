using Application.Abstractions.Messaging;
using Application.Features.AiModels.GetAllAiModels;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.GetUserAiModelsEnabled;

public sealed class
    GetEnabledAiModelsByUserIdQueryHandler : IQueryHandler<GetEnabledAiModelsByUserIdQuery, IReadOnlyList<AiModelDto>>
{
    private readonly IAiModelRepository _aiModelRepository;

    public GetEnabledAiModelsByUserIdQueryHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<IReadOnlyList<AiModelDto>>> Handle(GetEnabledAiModelsByUserIdQuery request,
        CancellationToken cancellationToken)
    {
        var aiModels = await _aiModelRepository.GetEnabledByUserIdAsync(request.UserId);

        var result = aiModels.Select(a => new AiModelDto(a.Id, a.Name, a.IsEnabled)).ToList();
        return Result.Success<IReadOnlyList<AiModelDto>>(result);
    }
}