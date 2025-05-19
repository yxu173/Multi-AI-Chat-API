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
        // Create a new ModelParameters with updated values and assign it back
        ModelParameters = ModelParameters.WithUpdates(
            defaultModel: defaultModel,
            systemInstructions: systemMessage,
            temperature: temperature,
            presencePenalty: presencePenalty,
            frequencyPenalty: frequencyPenalty,
            topP: topP,
            topK: topK,
            maxTokens: maxTokens,
            contextLimit: contextLimit,
            promptCaching: promptCaching,
            safetySettings: safetySettings
        );
    }
    
    /// <summary>
    /// Updates the entire model parameters object with a new instance
    /// </summary>
    /// <param name="newParameters">The new model parameters to use</param>
    public void UpdateModelParameters(ModelParameters newParameters)
    {
        if (newParameters == null)
            throw new ArgumentNullException(nameof(newParameters));
            
        ModelParameters = newParameters;
    }
}