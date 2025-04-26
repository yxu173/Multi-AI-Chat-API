using Domain.Common;

namespace Domain.Aggregates.Chats;

public class Plugin : BaseEntity
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string IconUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    
    public IReadOnlyCollection<ChatSessionPlugin> ChatSessionPlugins => _chatSessionPlugins;
    private readonly List<ChatSessionPlugin> _chatSessionPlugins = new();

    private Plugin()
    {
    }

    public static Plugin Create(string name, string description, string iconUrl = "/icon.png")
    {
        return new Plugin
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IconUrl = iconUrl,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string description, string iconUrl)
    {
        Name = name;
        Description = description;
        IconUrl = iconUrl;
        UpdatedAt = DateTime.UtcNow;
    }
}