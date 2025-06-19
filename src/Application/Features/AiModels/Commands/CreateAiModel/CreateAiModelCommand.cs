using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.Commands.CreateAiModel;

public sealed record CreateAiModelCommand(
    string Name,
    string ModelType,
    Guid AiProvider,
    double InputTokenPricePer1M,
    double OutputTokenPricePer1M,
    string ModelCode,
    int MaxOutputTokens,
    bool IsEnabled,
    bool SupportsThinking,
    bool SupportsVision,
    int ContextLength,
    bool PromptCachingSupported,
    double RequestCost) : ICommand<Guid>;