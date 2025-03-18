using Domain.Common;

namespace Domain.Aggregates.Users;

public sealed class UserAiModelSettings : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid AiModelId { get; private set; }
    public int? MaxInputTokens { get; private set; }
    public int? MaxOutputTokens { get; private set; }
    public double? Temperature { get; private set; }
    public double? TopP { get; private set; }
    public int? TopK { get; private set; }
    public double? FrequencyPenalty { get; private set; }
    public double? PresencePenalty { get; private set; }
    public bool IsDefault { get; private set; }
    
    public User User { get; private set; } = null!;
    public UserAiModel UserAiModel { get; private set; } = null!;

    private UserAiModelSettings()
    {
    }

    public static UserAiModelSettings Create(
        Guid userId, 
        Guid aiModelId, 
        int? maxInputTokens = null, 
        int? maxOutputTokens = null,
        double? temperature = 0.7,
        double? topP = 1.0,
        int? topK = null,
        double? frequencyPenalty = 0.0,
        double? presencePenalty = 0.0,
        bool isDefault = false)
    {
        return new UserAiModelSettings
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AiModelId = aiModelId,
            MaxInputTokens = maxInputTokens,
            MaxOutputTokens = maxOutputTokens,
            Temperature = temperature,
            TopP = topP,
            TopK = topK,
            FrequencyPenalty = frequencyPenalty,
            PresencePenalty = presencePenalty,
            IsDefault = isDefault
        };
    }

    public void UpdateSettings(
        int? maxInputTokens = null,
        int? maxOutputTokens = null,
        double? temperature = null,
        double? topP = null,
        int? topK = null,
        double? frequencyPenalty = null,
        double? presencePenalty = null)
    {
        if (maxInputTokens.HasValue) MaxInputTokens = maxInputTokens;
        if (maxOutputTokens.HasValue) MaxOutputTokens = maxOutputTokens;
        if (temperature.HasValue) Temperature = temperature;
        if (topP.HasValue) TopP = topP;
        if (topK.HasValue) TopK = topK;
        if (frequencyPenalty.HasValue) FrequencyPenalty = frequencyPenalty;
        if (presencePenalty.HasValue) PresencePenalty = presencePenalty;
    }

    public void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
    }
}
