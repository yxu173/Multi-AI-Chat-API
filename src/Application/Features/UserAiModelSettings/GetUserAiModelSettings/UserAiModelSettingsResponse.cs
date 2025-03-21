namespace Application.Features.UserAiModelSettings.GetUserAiModelSettings;

public sealed record UserAiModelSettingsResponse(
    Guid Id,
    Guid UserId,
    double? Temperature,
    double? TopP,
    int? TopK,
    double? FrequencyPenalty,
    double? PresencePenalty
);