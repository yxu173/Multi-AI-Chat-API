using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using Domain.Repositories;
using SharedKernal;
using Domain.Enums;

namespace Application.Features.AiModels.Commands.CreateAiModel;

public sealed class CreateAiModelCommandHandler : ICommandHandler<CreateAiModelCommand, Guid>
{
    private readonly IAiModelRepository _aiModelRepository;
    private readonly IAiProviderRepository _aiProviderRepository;

    public CreateAiModelCommandHandler(IAiModelRepository aiModelRepository, IAiProviderRepository aiProviderRepository)
    {
        _aiModelRepository = aiModelRepository;
        _aiProviderRepository = aiProviderRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(CreateAiModelCommand request, CancellationToken ct)
    {
        var provider = await _aiProviderRepository.GetByNameAsync(request.ModelType);
        
        if (provider == null)
        {
            return Result.Failure<Guid>(Error.NotFound("ProviderNotFound", $"Provider '{provider}' not found for model type '{request.ModelType}'"));
        }

        var aiModel = AiModel.Create(
            request.Name,
            request.ModelType,
            provider.Id,
            request.InputTokenPricePer1M,
            request.OutputTokenPricePer1M,
            request.ModelCode,
            request.ContextLength,
            request.RequestCost,
            request.MaxOutputTokens,
            request.IsEnabled,
            request.SupportsThinking,
            request.SupportsVision,
            request.PromptCachingSupported
        );
        await _aiModelRepository.AddAsync(aiModel);
        return Result.Success(aiModel.Id);
    }

}