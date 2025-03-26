using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.Commands.UserEnableAiModel;

public sealed record UserEnableAiModelCommand(Guid UserId, Guid AiModelId) : ICommand<bool>;