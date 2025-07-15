namespace Application.Features.UserAiModelSettings.GetUserAiModelSettings;

public sealed record UserAiModelSettingsResponse(
    Guid Id,
    Guid UserId,
    string ModelName,
    string SystemMessage,
    double? Temperature,
    int? MaxTokens
);