using System.Text.Json.Serialization;
using Domain.Aggregates.Users;
using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class AiModel
{
    [JsonIgnore]
    private readonly List<UserAiModel> _userAiModels = new();
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ModelType ModelType { get; private set; }
    public double InputTokenPricePer1M { get; private set; }
    public double OutputTokenPricePer1M { get; private set; }
    public Guid AiProviderId { get; private set; }
    [JsonIgnore]
    public AiProvider AiProvider { get; private set; }
    public string ModelCode { get; private set; }
    public int? MaxInputTokens { get; private set; }
    public int? MaxOutputTokens { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public bool SupportsThinking { get; private set; }
    public bool SupportsVision { get; private set; }
    public int ContextLength { get; private set; }
    public string ApiType { get; private set; }
    public bool PluginsSupported { get; private set; }
    public bool StreamingOutputSupported { get; private set; }
    public bool SystemRoleSupported { get; private set; }
    public bool PromptCachingSupported { get; private set; }

    [JsonIgnore]
    public IReadOnlyCollection<UserAiModel> UserAiModels => _userAiModels;

    private AiModel()
    {
    }

    [JsonConstructor]
    public AiModel(Guid id, string name, ModelType modelType, Guid aiProviderId, double inputTokenPricePer1M,
        double outputTokenPricePer1M, string modelCode, int? maxInputTokens, int? maxOutputTokens,
        bool isEnabled, bool supportsThinking, bool supportsVision, int contextLength, string apiType,
        bool pluginsSupported, bool streamingOutputSupported, bool systemRoleSupported, bool promptCachingSupported)
    {
        Id = id;
        Name = name;
        ModelType = modelType;
        AiProviderId = aiProviderId;
        InputTokenPricePer1M = inputTokenPricePer1M;
        OutputTokenPricePer1M = outputTokenPricePer1M;
        ModelCode = modelCode;
        MaxInputTokens = maxInputTokens;
        MaxOutputTokens = maxOutputTokens;
        IsEnabled = isEnabled;
        SupportsThinking = supportsThinking;
        SupportsVision = supportsVision;
        ContextLength = contextLength;
        ApiType = apiType;
        PluginsSupported = pluginsSupported;
        StreamingOutputSupported = streamingOutputSupported;
        SystemRoleSupported = systemRoleSupported;
        PromptCachingSupported = promptCachingSupported;
    }

    public static AiModel Create(string name, string modelType, Guid aiProviderId, double inputTokenPricePer1M,
        double outputTokenPricePer1M, string modelCode, int contextLength,int? maxInputTokens = null, int? maxOutputTokens = null,
        bool isEnabled = true, bool supportsThinking = false, bool supportsVision = false,
         string apiType = null, bool pluginsSupported = false,
        bool streamingOutputSupported = false, bool systemRoleSupported = false, bool promptCachingSupported = false)
    {
        var modelTypeEnum = Enum.Parse<ModelType>(modelType);
        return new AiModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            ModelType = modelTypeEnum,
            AiProviderId = aiProviderId,
            InputTokenPricePer1M = inputTokenPricePer1M,
            OutputTokenPricePer1M = outputTokenPricePer1M,
            ModelCode = modelCode,
            MaxInputTokens = maxInputTokens,
            MaxOutputTokens = maxOutputTokens,
            IsEnabled = isEnabled,
            SupportsThinking = supportsThinking,
            SupportsVision = supportsVision,
            ContextLength = contextLength,
            ApiType = apiType,
            PluginsSupported = pluginsSupported,
            StreamingOutputSupported = streamingOutputSupported,
            SystemRoleSupported = systemRoleSupported,
            PromptCachingSupported = promptCachingSupported
        };
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }

    public void SetSupportsThinking(bool supportsThinking)
    {
        SupportsThinking = supportsThinking;
    }

    public decimal CalculateCost(int inputTokens, int outputTokens)
    {
        var inputCost = (decimal)(inputTokens * InputTokenPricePer1M / 1_000_000);
        var outputCost = (decimal)(outputTokens * OutputTokenPricePer1M / 1_000_000);
        return inputCost + outputCost;
    }
}