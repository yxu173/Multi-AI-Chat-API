namespace Application.Features.AiModels.DTOs;

public sealed record DetailedAiModelDto(
    Guid ModelId,
    string Name,
    bool IsEnabled,
    string ModelType,
    Guid AiProviderId,
    string ModelCode,
    double InputTokenPricePer1M,
    double OutputTokenPricePer1M,
    int? MaxOutputTokens,
    bool SupportsThinking,
    bool SupportsVision,
    int? ContextLength,
    bool PromptCachingSupported
);