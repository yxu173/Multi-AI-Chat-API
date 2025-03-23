using Domain.Aggregates.Users;
using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class AiModel
{
    private readonly List<UserAiModel> _userAiModels = new();
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ModelType ModelType { get; private set; }
    public double InputTokenPricePer1M { get; private set; }
    public double OutputTokenPricePer1M { get; private set; }
    public Guid AiProviderId { get; private set; }
    public AiProvider AiProvider { get; private set; }
    public string ModelCode { get; private set; }
    public int? MaxInputTokens { get; private set; }
    public int? MaxOutputTokens { get; private set; }
    public bool IsEnabled { get; private set; } = true;

    public IReadOnlyCollection<UserAiModel> UserAiModels => _userAiModels;


    private AiModel()
    {
    }

    public static AiModel Create(string name, string modelType, Guid aiProviderId, double inputTokenPricePer1M,
        double outputTokenPricePer1M, string modelCode, int? maxInputTokens = null, int? maxOutputTokens = null,
        bool isEnabled = true)
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
            IsEnabled = isEnabled
        };
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }
    public decimal CalculateCost(int inputTokens, int outputTokens)
    {
        var inputCost = (decimal)(inputTokens * InputTokenPricePer1M / 1_000_000);
        var outputCost = (decimal)(outputTokens * OutputTokenPricePer1M / 1_000_000);
        return inputCost + outputCost;
    }
}