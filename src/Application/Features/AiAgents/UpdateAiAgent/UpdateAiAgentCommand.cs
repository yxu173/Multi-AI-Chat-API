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
    double? PresencePenalty = null,
    double? FrequencyPenalty = null,
    double? TopP = null,
    int? TopK = null,
    int? MaxTokens = null,
    bool? EnableThinking = null,
    bool? PromptCaching = null,
    int? ContextLimit = null,
    string? SafetySettings = null,
    string? ProfilePictureUrl = null,
    List<PluginInfo>? Plugins = null) : ICommand<bool>;