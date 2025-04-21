using Application.Abstractions.Messaging;

namespace Application.Features.UserAiModelSettings.UpdateUserAiModelSettings;

public sealed record UpdateUserAiModelSettingsCommand(
    Guid UserId,
    Guid AiModelId,
    string SystemMessage,
    int? ContextLimit,
    double? Temperature,
    double? TopP,
    int? TopK,
    double? FrequencyPenalty,
    double? PresencePenalty,
    int? MaxTokens,
    string SafetySettings,
    bool? PromptCaching
) : ICommand<bool>;