namespace Web.Api.Contracts.AiAgents;

public sealed record CreateAiAgentRequest(
    string Name,
    string Description,
    string SystemPrompt,
    Guid AiModelId,
    string? IconUrl,
    List<string>? Categories,
    bool AssignCustomModelParameters,
    string? ModelParameters,
    string? ProfilePictureUrl,
    List<PluginRequest>? Plugins);
public sealed record PluginRequest(Guid PluginId, int Order, bool IsActive);
