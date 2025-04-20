using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.Commands.EnableAiModel;

public sealed record EnableAiModelCommand(Guid ModelId) : ICommand<bool>;