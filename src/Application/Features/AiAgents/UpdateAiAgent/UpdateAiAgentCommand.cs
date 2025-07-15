using Application.Abstractions.Messaging;
using Application.Features.AiAgents.CreateAiAgent;
using Domain.ValueObjects;

namespace Application.Features.AiAgents.UpdateAiAgent;

public sealed record UpdateAiAgentCommand(
    Guid UserId,
    Guid AiAgentId,
    string Name,
    string Description,
    string? SystemInstructions,
    Guid AiModelId,
    List<string>? Categories,
    bool? AssignCustomModelParameters,
    double? Temperature = null,
    int? MaxTokens = null,
    string? ProfilePictureUrl = null,
    List<PluginInfo>? Plugins = null) : ICommand<bool>;