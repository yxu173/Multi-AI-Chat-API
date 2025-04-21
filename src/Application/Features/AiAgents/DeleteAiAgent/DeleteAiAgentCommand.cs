using Application.Abstractions.Messaging;

namespace Application.Features.AiAgents.DeleteAiAgent;

public sealed record DeleteAiAgentCommand(Guid UserId, Guid AiAgentId) : ICommand<bool>;