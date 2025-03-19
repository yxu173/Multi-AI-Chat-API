namespace Application.Features.AiAgents.CreateAiAgent;

public sealed record CreateAiAgentResponse(Guid Id, string Name, string Description, Guid AiModelId, string ModelName);