using Application.Abstractions.Messaging;
using Application.Features.AiModels.DTOs;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiModels.Queries.GetAiModelById;

public sealed class GetAiModelByIdQueryHandler : IQueryHandler<GetAiModelByIdQuery, DetailedAiModelDto>
{
    private readonly IAiModelRepository _aiModelRepository;

    public GetAiModelByIdQueryHandler(IAiModelRepository aiModelRepository)
    {
        _aiModelRepository = aiModelRepository;
    }

    public async Task<Result<DetailedAiModelDto>> ExecuteAsync(GetAiModelByIdQuery request, CancellationToken ct)

    {
        var aiModel = await _aiModelRepository.GetByIdAsync(request.ModelId);

        if (aiModel == null)
        {
            return Result.Failure<DetailedAiModelDto>(Error.NotFound(
                "AiModel.NotFound",
                $"AI Model with ID {request.ModelId} not found."));
        }

        var result = new DetailedAiModelDto(
            aiModel.Id,
            aiModel.Name,
            aiModel.IsEnabled,
            aiModel.ModelType.ToString(),
            aiModel.AiProviderId,
            aiModel.ModelCode,
            aiModel.InputTokenPricePer1M,
            aiModel.OutputTokenPricePer1M,
            aiModel.MaxOutputTokens,
            aiModel.SupportsThinking,
            aiModel.SupportsVision,
            aiModel.ContextLength,
            aiModel.PluginsSupported,
            aiModel.SystemRoleSupported,
            aiModel.PromptCachingSupported
        );

        return Result.Success(result);
    }
}