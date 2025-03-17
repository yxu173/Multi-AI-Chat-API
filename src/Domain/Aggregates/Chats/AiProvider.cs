using Domain.Aggregates.Chats;

public sealed class AiProvider
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string DefaultApiKey { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    
    
    private readonly List<AiModel> _models = new();
    public IReadOnlyList<AiModel> Models => _models.AsReadOnly();

    private AiProvider() { } 

    public static AiProvider Create(string name, string description, string defaultApiKey = "")
    {
        return new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            DefaultApiKey = defaultApiKey,
            IsEnabled = true
        };
    }
    
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }
    
    public void UpdateApiKey(string apiKey)
    {
        DefaultApiKey = apiKey;
    }
}