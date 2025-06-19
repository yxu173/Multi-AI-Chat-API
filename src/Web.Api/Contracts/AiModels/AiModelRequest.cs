namespace Web.Api.Contracts.AiModels;

public sealed record AiModelRequest(
    string Name,
    string ModelType,
    Guid AiProviderId,
    double InputTokenPricePer1M,
    double OutputTokenPricePer1M,
    string ModelCode,
    double RequestCost,
    int MaxOutputTokens,
    bool IsEnabled,
    bool SupportsThinking,
    bool SupportsVision,
    int ContextLength,
    bool PromptCachingSupported
);