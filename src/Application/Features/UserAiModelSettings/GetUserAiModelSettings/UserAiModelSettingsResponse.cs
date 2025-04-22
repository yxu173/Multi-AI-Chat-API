namespace Application.Features.UserAiModelSettings.GetUserAiModelSettings;

public sealed record UserAiModelSettingsResponse(
    Guid Id,
    Guid UserId,
    string ModelName,
    string SystemMessage,
    int? ContextLimit,
    double? Temperature,
    double? TopP,
    int? TopK,
    double? FrequencyPenalty,
    double? PresencePenalty,
    int? MaxTokens,
    string SafetySettings,
    bool PromptCaching
);