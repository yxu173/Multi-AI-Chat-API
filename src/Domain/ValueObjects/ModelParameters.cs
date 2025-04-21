using Domain.Common;
using System.Text.Json;

namespace Domain.ValueObjects;

public record ModelParameters : ValueObject
{
    public string SystemInstructions { get; init; }
    public Guid DefaultModel { get; private set; }
    public int ContextLimit { get; init; }
    public double Temperature { get; init; }
    public double PresencePenalty { get; init; }
    public double FrequencyPenalty { get; init; }
    public double TopP { get; init; }
    public int TopK { get; init; }
    public int MaxTokens { get; init; }
    public List<string>? StopSequences { get; init; }
    public bool PromptCaching { get; init; }
    public string? SafetySettings { get; init; }

    private ModelParameters()
    {
    }

    public static ModelParameters Create(
        Guid defaultModel = default,
        string systemInstructions = null,
        double? temperature = null,
        double? presencePenalty = null,
        double? frequencyPenalty = null,
        double? topP = null,
        int? topK = null,
        int? maxTokens = null,
        bool? enableThinking = null,
        List<string>? stopSequences = null,
        bool? promptCaching = null,
        int? contextLimit = null,
        string? safetySettings = null)
    {
        return new ModelParameters
        {
            SystemInstructions = systemInstructions ?? "you are a helpful assistant",
            DefaultModel = defaultModel == default ? new Guid("e29a994d-617f-49b8-8bff-17dcb9a08462") : defaultModel,
            Temperature = temperature ?? 0.7,
            PresencePenalty = presencePenalty ?? 0.0,
            FrequencyPenalty = frequencyPenalty ?? 0.0,
            TopP = topP ?? 1.0,
            TopK = topK ?? 40,
            MaxTokens = maxTokens ?? 1000,
            StopSequences = stopSequences ?? new List<string>(),
            PromptCaching = promptCaching ?? false,
            ContextLimit = contextLimit ?? 2, //TODO: Make it 0 for no limit
            SafetySettings = safetySettings ?? string.Empty,
        };
    }
    
    
    public ModelParameters UpdateModelParameters(
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
    
    public static ModelParameters FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON cannot be empty", nameof(json));
            
        var parameters = JsonSerializer.Deserialize<ModelParameters>(json);
        return parameters ?? Create();
    }
    
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Temperature;
        yield return PresencePenalty;
        yield return FrequencyPenalty;
        yield return TopP;
        yield return TopK;
        yield return MaxTokens;
        yield return ContextLimit;
        yield return PromptCaching;
        yield return SafetySettings;
        
        if (StopSequences != null)
        {
            foreach (var sequence in StopSequences)
            {
                yield return sequence;
            }
        }
    }
} 