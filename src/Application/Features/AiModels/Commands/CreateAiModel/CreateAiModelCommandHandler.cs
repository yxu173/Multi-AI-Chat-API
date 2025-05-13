using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.Commands.CreateAiModel;

public sealed class CreateAiModelCommandHandler : ICommandHandler<CreateAiModelCommand, Guid>
{
    private readonly IAiModelRepository _aiModelRepository;

    public CreateAiModelCommandHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(CreateAiModelCommand request, CancellationToken ct)
    {
        var aiModel = AiModel.Create(
            request.Name,
            request.ModelType,
            request.AiProvider,
            request.InputTokenPricePer1M,
            request.OutputTokenPricePer1M,
            request.ModelCode,
            request.ContextLength,
            request.MaxOutputTokens,
            request.IsEnabled,
            request.SupportsThinking,
            request.SupportsVision,
            request.PluginsSupported,
            request.SystemRoleSupported,
            request.PromptCachingSupported
        );
        await _aiModelRepository.AddAsync(aiModel);
        return Result.Success(aiModel.Id);
    }
}