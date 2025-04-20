namespace Web.Api.Contracts.AiModels;

public sealed record AiModelRequest(
    string Name,
    string ModelType,
    Guid AiProviderId,
    double InputTokenPricePer1M,
    double OutputTokenPricePer1M,
    string ModelCode,
    int MaxInputTokens,
    int MaxOutputTokens,
    bool IsEnabled,
    bool SupportsThinking,
    bool SupportsVision,
    int? ContextLength,
    string ApiType,
    bool PluginsSupported,
    bool StreamingOutputSupported,
    bool SystemRoleSupported,
    bool PromptCachingSupported
);