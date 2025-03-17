namespace Web.Api.Contracts.AiModels;

public sealed record AiModelRequest(
    string Name,
    string ModelType,
    Guid AiProvider,
    double InputTokenPricePer1K,
    double OutputTokenPricePer1K,
    string ModelCode
);