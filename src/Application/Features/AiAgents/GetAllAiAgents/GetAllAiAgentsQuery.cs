using Application.Abstractions.Messaging;

namespace Application.Features.AiAgents.GetAllAiAgents;

public record GetAllAiAgentsQuery(Guid UserId) : IQuery<List<AiAgentResponse>>; 