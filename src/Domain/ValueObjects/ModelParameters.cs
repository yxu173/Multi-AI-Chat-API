using Domain.Common;
using System.Text.Json;

namespace Domain.ValueObjects;

public record ModelParameters : ValueObject
{
    public string? ContextLimit { get; init; }
    public double? Temperature { get; init; }
    public double? PresencePenalty { get; init; }
    public double? FrequencyPenalty { get; init; }
    public double? TopP { get; init; }
    public int? TopK { get; init; }
    public int? MaxTokens { get; init; }
    public bool? EnableThinking { get; init; }
    public List<string>? StopSequences { get; init; }
    public bool? PromptCaching { get; init; }
    public string? SafetySettings { get; init; }

    private ModelParameters()
    {
    }

    public static ModelParameters Create(
        double? temperature = null,
        double? presencePenalty = null,
        double? frequencyPenalty = null,
        double? topP = null,
        int? topK = null,
        int? maxTokens = null,
        bool? enableThinking = null,
        List<string>? stopSequences = null,
        bool? promptCaching = null,
        string? contextLimit = null,
        string? safetySettings = null)
    {
        return new ModelParameters
        {
            Temperature = temperature,
            PresencePenalty = presencePenalty,
            FrequencyPenalty = frequencyPenalty,
            TopP = topP,
            TopK = topK,
            MaxTokens = maxTokens,
            EnableThinking = enableThinking,
            StopSequences = stopSequences,
            PromptCaching = promptCaching,
            ContextLimit = contextLimit,
            SafetySettings = safetySettings
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
        yield return EnableThinking;
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