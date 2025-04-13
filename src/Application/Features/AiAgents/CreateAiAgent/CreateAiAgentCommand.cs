using Application.Abstractions.Messaging;
using Domain.ValueObjects;

namespace Application.Features.AiAgents.CreateAiAgent;

public sealed record CreateAiAgentCommand(
    Guid UserId,
    string Name,
    string Description,
    string SystemInstructions,
    Guid DefaultModel,
    string? IconUrl,
    List<string>? Categories,
    bool AssignCustomModelParameters,
    double? Temperature = null,
    double? PresencePenalty = null,
    double? FrequencyPenalty = null,
    double? TopP = null,
    int? TopK = null,
    int? MaxTokens = null,
    bool? EnableThinking = null,
    List<string>? StopSequences = null,
    bool? PromptCaching = null,
    string? ContextLimit = null,
    string? SafetySettings = null,
    string? ProfilePictureUrl = null,
    List<PluginInfo>? Plugins = null) : ICommand<Guid>;

public record PluginInfo(Guid PluginId, bool IsActive);