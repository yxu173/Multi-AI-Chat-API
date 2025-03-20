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
    public bool IsDefault { get; private set; }
    public List<string> StopSequences { get; private set; } = new List<string>();

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
            IsDefault = false,
            StopSequences = new List<string>()
        };
    }

    public void UpdateSettings(
        double? temperature = null,
        double? topP = null,
        int? topK = null,
        double? frequencyPenalty = null,
        double? presencePenalty = null)
    {
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