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
    int? MaxInputTokens,
    int? MaxOutputTokens,
    bool SupportsThinking,
    bool SupportsVision,
    int? ContextLength,
    string ApiType,
    bool PluginsSupported,
    bool StreamingOutputSupported,
    bool SystemRoleSupported,
    bool PromptCachingSupported
);