using Domain.Aggregates.Chats;

public sealed class AiProvider
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string BaseUrl { get; private set; } 
    public string DefaultApiKey { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    
    // Navigation property
    private readonly List<AiModel> _models = new();
    public IReadOnlyList<AiModel> Models => _models.AsReadOnly();

    private AiProvider() { } // Required for EF Core

    public static AiProvider Create(string name, string description, string baseUrl, string defaultApiKey = "")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("BaseUrl cannot be empty.", nameof(baseUrl));

        return new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            BaseUrl = baseUrl,
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