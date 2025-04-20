namespace Application.Features.AiAgents.GetAllAiAgents;

public record GetAllAiAgentResponse(
    Guid Id,
    string Name,
    string Description,
    string? ProfilePictureUrl);