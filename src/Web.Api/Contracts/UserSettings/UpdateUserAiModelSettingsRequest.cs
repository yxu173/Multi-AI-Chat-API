namespace Web.Api.Contracts.UserSettings;

public record UpdateUserAiModelSettingsRequest(
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
);