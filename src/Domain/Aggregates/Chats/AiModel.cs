using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class AiModel
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ModelType ModelType { get; private set; }
    public string ApiKey { get; private set; }
    public double InputTokenPricePer1K { get; private set; }
    public double OutputTokenPricePer1K { get; private set; }

    private AiModel()
    {
    }

    public static AiModel Create(string name, ModelType modelType, string apiKey, double inputTokenPricePer1K,
        double outputTokenPricePer1K)
    {
        return new AiModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            ModelType = modelType,
            ApiKey = apiKey,
            InputTokenPricePer1K = inputTokenPricePer1K,
            OutputTokenPricePer1K = outputTokenPricePer1K
        };
    }
}