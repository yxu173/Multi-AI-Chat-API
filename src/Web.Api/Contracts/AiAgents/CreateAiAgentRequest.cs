namespace Web.Api.Contracts.AiAgents;

public record CreateAiAgentRequest(
    string Name,
    string Description,
    string? SystemInstructions,
    Guid AiModelId,
    string? IconUrl = null,
    List<string>? Categories = null,
    bool AssignCustomModelParameters = false,
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
    List<PluginRequest>? Plugins = null
);

public record PluginRequest(
    Guid PluginId,
    int Order,
    bool IsActive = true
);
