namespace Application.Features.AiAgents.GetAllAiAgents;

public record AiAgentResponse(
    Guid Id,
    string Name,
    string Description,
    string? SystemInstructions,
    Guid AiModelId,
    string AiModelName,
    string? IconUrl,
    List<string> Categories,
    bool AssignCustomModelParameters,
    string? ModelParameters,
    string? ProfilePictureUrl,
    List<AgentPluginResponse>? Plugins
);

public record AgentPluginResponse(
    Guid PluginId,
    string PluginName,
    bool IsActive
); 