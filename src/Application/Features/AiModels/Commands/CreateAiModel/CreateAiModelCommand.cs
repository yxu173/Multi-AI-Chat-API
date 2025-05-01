using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.Commands.CreateAiModel;

public sealed record CreateAiModelCommand(
    string Name,
    string ModelType,
    Guid AiProvider,
    double InputTokenPricePer1M,
    double OutputTokenPricePer1M,
    string ModelCode,
    int MaxInputTokens,
    int MaxOutputTokens,
    bool IsEnabled,
    bool SupportsThinking,
    bool SupportsVision,
    int ContextLength,
    string ApiType,
    bool PluginsSupported,
    bool StreamingOutputSupported,
    bool SystemRoleSupported,
    bool PromptCachingSupported) : ICommand<Guid>;