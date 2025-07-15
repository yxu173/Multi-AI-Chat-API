using Domain.Common;
using System.Text.Json;

namespace Domain.ValueObjects;

public sealed record ModelParameters(
    string SystemInstructions,
    Guid DefaultModel,
    double Temperature,
    int MaxTokens)
{
    public static ModelParameters Create(
        Guid? defaultModel = null,
        string? systemInstructions = null,
        double? temperature = null,
        int? maxTokens = null)
    {
        return new ModelParameters(
            SystemInstructions: systemInstructions ?? "you are a helpful assistant",
            DefaultModel: defaultModel ?? new Guid("e29a994d-617f-49b8-8bff-17dcb9a08462"),
            Temperature: temperature ?? 0.7,
            MaxTokens: maxTokens ?? 1000
        );
    }

    public ModelParameters WithDefaultSystemInstructions() =>
        this with { SystemInstructions = "you are a helpful assistant" };

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
            MaxTokens = maxTokens ?? MaxTokens
        };
    }

    public static ModelParameters FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON cannot be empty", nameof(json));

        var parameters = JsonSerializer.Deserialize<ModelParameters>(json);
        return parameters ?? Create();
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}