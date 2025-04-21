namespace Web.Api.Contracts.AiAgents;

public record CreateAiAgentRequest(
    string Name,
    string Description,
    string? SystemInstructions,
    Guid DefaultModel,
    List<string>? Categories = null,
    bool AssignCustomModelParameters = false,
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
    List<PluginRequest>? Plugins = null
);

public record PluginRequest(
    Guid PluginId,
    bool IsActive = true
);
