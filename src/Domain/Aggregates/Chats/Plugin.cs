using Domain.Common;

namespace Domain.Aggregates.Chats;

public class Plugin : BaseEntity
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string IconUrl { get; private set; }
    public string PluginType { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Plugin()
    {
    }

    public static Plugin Create(string name, string description, string pluginType, string iconUrl = "/icon.png")
    {
        return new Plugin
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            PluginType = pluginType,
            IconUrl = iconUrl,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string description, string pluginType, string iconUrl)
    {
        Name = name;
        Description = description;
        PluginType = pluginType;
        IconUrl = iconUrl;
        UpdatedAt = DateTime.UtcNow;
    }
}