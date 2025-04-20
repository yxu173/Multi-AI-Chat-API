namespace Application.Features.AiAgents.GetAllAiAgents;

public record GetAllAiAgentsGroupedByCategoryResponse(
    Dictionary<string, List<GetAllAiAgentResponse>> AgentsByCategory
);