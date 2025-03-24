using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.CreateAiModel;

public sealed record CreateAiModelCommand(
    string Name,
    string ModelType,
    Guid AiProvider,
    double InputTokenPricePer1K,
    double OutputTokenPricePer1K,
    string ModelCode,
    int MaxInputTokens,
    int MaxOutputTokens,
    bool IsEnabled,
    bool SupportsThinking) : ICommand<Guid>;