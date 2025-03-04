using Application.Abstractions.Messaging;

namespace Application.Features.AiModels.CreateAiModel;

public sealed record CreateAiModelCommand() : ICommand<bool>;