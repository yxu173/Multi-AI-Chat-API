using System.Text.Json.Serialization;
using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Aggregates.Users;

public sealed class UserAiModelSettings : BaseEntity
{
    public Guid UserId { get; private set; }
    public ModelParameters ModelParameters { get; private set; } = null!;


    [JsonIgnore]
    public User User { get; private set; } = null!;

    private UserAiModelSettings()
    {
    }

    [JsonConstructor]
    public UserAiModelSettings( Guid userId, ModelParameters modelParameters)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ModelParameters = modelParameters;
    }

    public static UserAiModelSettings Create(
        Guid userId)
    {
        return new UserAiModelSettings
        {
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
        int? contextLimit,
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