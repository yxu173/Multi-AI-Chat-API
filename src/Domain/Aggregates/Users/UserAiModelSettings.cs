using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Aggregates.Users;

public sealed class UserAiModelSettings : BaseEntity
{
    public Guid UserId { get; private set; }
    public ModelParameters ModelParameters { get; private set; } = null!;

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
            ModelParameters = ModelParameters.Create()
        };
    }

    public void UpdateSettings(
        Guid defaultModel,
        string? systemMessage,
        double? temperature,
        double? topP,
        int? topK,
        double? frequencyPenalty,
        double? presencePenalty,
        string? contextLimit,
        int? maxTokens,
        string? safetySettings,
        bool? promptCaching)
    {
        this.ModelParameters.UpdateModelParameters(
            defaultModel,
            systemMessage,
            temperature,
            presencePenalty,
            frequencyPenalty,
            topP,
            topK,
            maxTokens,
            contextLimit,
            promptCaching,
            safetySettings
        );
    }
}