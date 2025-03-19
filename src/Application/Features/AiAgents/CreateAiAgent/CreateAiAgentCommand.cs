using Application.Abstractions.Messaging;

namespace Application.Features.AiAgents.CreateAiAgent;

public sealed record CreateAiAgentCommand(
    Guid UserId,
    string Name,
    string Description,
    string SystemPrompt,
    Guid AiModelId,
    string? IconUrl,
    List<string>? Categories,
    bool AssignCustomModelParameters,
    string? ModelParameters,
    string? ProfilePictureUrl,
    List<PluginInfo>? Plugins) : ICommand<Guid>;

public record PluginInfo(Guid PluginId, int Order, bool IsActive);