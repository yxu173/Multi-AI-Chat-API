using Application.Abstractions.Messaging;
using Application.Features.AiAgents.GetAllAiAgents;

namespace Application.Features.AiAgents.GetAiAgentById;

public record GetAiAgentByIdQuery(Guid UserId, Guid AiAgentId) : IQuery<AiAgentResponse>; 