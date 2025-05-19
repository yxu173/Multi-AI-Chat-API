using Domain.Common;
using System.Text.Json;

namespace Domain.ValueObjects;

// Immutable record: provides proper value equality by comparing all properties
public sealed record ModelParameters(
    string SystemInstructions,
    Guid DefaultModel,
    double Temperature,
    double PresencePenalty,
    double FrequencyPenalty,
    double TopP,
    int TopK,
    int MaxTokens,
    int ContextLimit,
    bool PromptCaching,
    string? SafetySettings)
{
    /// <summary>
    /// Creates a new ModelParameters with default values if not provided
    /// </summary>
    public static ModelParameters Create(
        Guid? defaultModel = null,
        string? systemInstructions = null,
        double? temperature = null,
        double? presencePenalty = null,
        double? frequencyPenalty = null,
        double? topP = null,
        int? topK = null,
        int? maxTokens = null,
        int? contextLimit = null,
        bool? promptCaching = null,
        string? safetySettings = null)
    {
        return new ModelParameters(
            SystemInstructions: systemInstructions ?? "you are a helpful assistant",
            DefaultModel: defaultModel ?? new Guid("e29a994d-617f-49b8-8bff-17dcb9a08462"),
            Temperature: temperature ?? 0.7,
            PresencePenalty: presencePenalty ?? 0.0,
            FrequencyPenalty: frequencyPenalty ?? 0.0,
            TopP: topP ?? 1.0,
            TopK: topK ?? 40,
            MaxTokens: maxTokens ?? 1000,
            ContextLimit: contextLimit ?? 2, //TODO: Make it 0 for no limit
            PromptCaching: promptCaching ?? false,
            SafetySettings: safetySettings ?? string.Empty
        );
    }

    /// <summary>
    /// Creates a new instance with SystemInstructions set to the default helpful assistant prompt
    /// </summary>
    public ModelParameters WithDefaultSystemInstructions() => 
        this with { SystemInstructions = "you are a helpful assistant" };
    
    /// <summary>
    /// Creates a new instance with updated parameters where specified
    /// </summary>
    public ModelParameters WithUpdates(
        Guid? defaultModel = null,
        string? systemInstructions = null,
        double? temperature = null,
        double? presencePenalty = null,
        double? frequencyPenalty = null,
        double? topP = null,
        int? topK = null,
        int? maxTokens = null,
        int? contextLimit = null,
        bool? promptCaching = null,
        string? safetySettings = null)
    {
        return this with
        {
            DefaultModel = defaultModel ?? DefaultModel,
            SystemInstructions = systemInstructions ?? SystemInstructions,
            Temperature = temperature ?? Temperature,
            PresencePenalty = presencePenalty ?? PresencePenalty,
            FrequencyPenalty = frequencyPenalty ?? FrequencyPenalty,
            TopP = topP ?? TopP,
            TopK = topK ?? TopK,
            MaxTokens = maxTokens ?? MaxTokens,
            ContextLimit = contextLimit ?? ContextLimit,
            PromptCaching = promptCaching ?? PromptCaching,
            SafetySettings = safetySettings ?? SafetySettings
        };
    }
    
    /// <summary>
    /// Deserializes ModelParameters from JSON
    /// </summary>
    public static ModelParameters FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON cannot be empty", nameof(json));
            
        var parameters = JsonSerializer.Deserialize<ModelParameters>(json);
        return parameters ?? Create();
    }
    
    /// <summary>
    /// Serializes to JSON
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    // No need for custom GetEqualityComponents as C# records automatically 
    // use all properties for equality comparisons
}