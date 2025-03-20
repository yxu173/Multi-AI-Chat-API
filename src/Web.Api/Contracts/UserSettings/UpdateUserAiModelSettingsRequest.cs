namespace Web.Api.Contracts.UserSettings;

public record UpdateUserAiModelSettingsRequest(
    double? Temperature,
    double? TopP,
    int? TopK,
    double? FrequencyPenalty,
    double? PresencePenalty
);