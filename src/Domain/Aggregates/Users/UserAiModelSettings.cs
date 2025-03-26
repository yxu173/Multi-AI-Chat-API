using Domain.Common;

namespace Domain.Aggregates.Users;

public sealed class UserAiModelSettings : BaseEntity
{
    public Guid UserId { get; private set; }
    public double? Temperature { get; private set; }
    public double? TopP { get; private set; }
    public int? TopK { get; private set; }
    public double? FrequencyPenalty { get; private set; }
    public double? PresencePenalty { get; private set; }
    public Guid DefaultModel { get; private set; }
    public List<string> StopSequences { get; private set; } = new List<string>();
    public string? SystemMessage { get; private set; }
    public int? ContextLimit { get; private set; }
    public int? MaxTokens { get; private set; }
    public string? SafetySettings { get; private set; }
    public bool PromptCaching { get; private set; }

    public User User { get; private set; } = null!;

    private UserAiModelSettings()
    {
    }

    public static UserAiModelSettings Create(
        Guid userId)
    {
        return new UserAiModelSettings
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Temperature = 0.7,
            TopP = 0.95f,
            TopK = 40,
            FrequencyPenalty = 0.0,
            PresencePenalty = 0.0,
            DefaultModel = Guid.NewGuid(), //TODO: Set default model
            StopSequences = new List<string>(),
            SystemMessage = "You are a helpful assistant that provides accurate and concise information.",
            ContextLimit = 0, // 0 means All (unlimited)
            MaxTokens = 1000,
            SafetySettings = "DEFAULT",
            PromptCaching = false
        };
    }

    public void UpdateSettings(
        double? temperature = null,
        double? topP = null,
        int? topK = null,
        double? frequencyPenalty = null,
        double? presencePenalty = null,
        string? systemMessage = null,
        int? contextLimit = null,
        int? maxTokens = null,
        string? safetySettings = null,
        bool? promptCaching = null)
    {
        if (temperature.HasValue) Temperature = temperature;
        if (topP.HasValue) TopP = topP;
        if (topK.HasValue) TopK = topK;
        if (frequencyPenalty.HasValue) FrequencyPenalty = frequencyPenalty;
        if (presencePenalty.HasValue) PresencePenalty = presencePenalty;
        if (systemMessage != null) SystemMessage = systemMessage;
        if (contextLimit.HasValue) ContextLimit = contextLimit;
        if (maxTokens.HasValue) MaxTokens = maxTokens;
        if (safetySettings != null) SafetySettings = safetySettings;
        if (promptCaching.HasValue) PromptCaching = promptCaching.Value;
    }
}