namespace Web.Api.Contracts.UserSettings;

public record UpdateUserAiModelSettingsRequest(
    string SystemMessage,
    double? Temperature,
    double? TopP,
    int? TopK,
    double? FrequencyPenalty,
    double? PresencePenalty
);