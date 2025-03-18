using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.CreateAiModel;

public sealed class CreateAiModelCommandHandler : ICommandHandler<CreateAiModelCommand, Guid>
{
    private readonly IAiModelRepository _aiModelRepository;

    public CreateAiModelCommandHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<Guid>> Handle(CreateAiModelCommand request, CancellationToken cancellationToken)
    {
        var aiModel = AiModel.Create(
            request.Name,
            request.ModelType,
            request.AiProvider,
            request.InputTokenPricePer1K,
            request.OutputTokenPricePer1K,
            request.ModelCode
        );
        await _aiModelRepository.AddAsync(aiModel);
        return Result.Success(aiModel.Id);
    }
}