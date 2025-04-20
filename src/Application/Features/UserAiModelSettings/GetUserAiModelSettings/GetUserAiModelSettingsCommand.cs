using Application.Abstractions.Messaging;

namespace Application.Features.UserAiModelSettings.GetUserAiModelSettings;

public sealed record GetUserAiModelSettingsCommand(Guid UserId) : ICommand<UserAiModelSettingsResponse>;