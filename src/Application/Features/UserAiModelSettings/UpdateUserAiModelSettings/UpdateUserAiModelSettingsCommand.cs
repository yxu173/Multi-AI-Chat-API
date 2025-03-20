using Application.Abstractions.Messaging;

namespace Application.Features.UserAiModelSettings.UpdateUserAiModelSettings;

public sealed record UpdateUserAiModelSettingsCommand(
    Guid UserId,
    double? Temperature,
    double? TopP,
    int? TopK,
    double? FrequencyPenalty,
    double? PresencePenalty
) : ICommand<bool>;