namespace Web.Api.Contracts.AiModels;

public sealed record AiModelRequest(
    string Name,
    string ModelType,
    Guid AiProviderId,
    double InputTokenPricePer1K,
    double OutputTokenPricePer1K,
    string ModelCode,
    int MaxInputTokens,
    int MaxOutputTokens,
    bool IsEnabled,
    bool SupportsThinking
);