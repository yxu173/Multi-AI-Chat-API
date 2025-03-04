using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class AiModel
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ModelType ModelType { get; private set; }
    public double InputTokenPricePer1K { get; private set; }
    public double OutputTokenPricePer1K { get; private set; }
    public Guid AiProviderId { get; private set; }
    public AiProvider AiProvider { get; private set; }
    public string ModelCode { get; private set; }
    public int? MaxInputTokens { get; private set; }
    public int? MaxOutputTokens { get; private set; }
    public bool IsEnabled { get; private set; } = true;

    private AiModel()
    {
    }

    public static AiModel Create(string name, string modelType, Guid aiProviderId, double inputTokenPricePer1K,
        double outputTokenPricePer1K, string modelCode, int? maxInputTokens = null, int? maxOutputTokens = null,
        bool isEnabled = true)
    {
        var modelTypeEnum = Enum.Parse<ModelType>(modelType);
        return new AiModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            ModelType = modelTypeEnum,
            AiProviderId = aiProviderId,
            InputTokenPricePer1K = inputTokenPricePer1K,
            OutputTokenPricePer1K = outputTokenPricePer1K,
            ModelCode = modelCode,
            MaxInputTokens = maxInputTokens,
            MaxOutputTokens = maxOutputTokens,
            IsEnabled = isEnabled
        };
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }
}