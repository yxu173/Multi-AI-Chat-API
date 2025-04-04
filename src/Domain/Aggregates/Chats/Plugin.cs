using Domain.Common;

namespace Domain.Aggregates.Chats;

public class Plugin : BaseEntity
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string IconUrl { get; private set; }
    public string PluginType { get; private set; }
    public string ParametersSchema { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Plugin()
    {
    }

    public static Plugin Create(string name, string description, string pluginType, string parametersSchema, string iconUrl = "/icon.png")
    {
        try { System.Text.Json.JsonDocument.Parse(parametersSchema); } catch { /* Handle invalid schema? */ }

        return new Plugin
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            PluginType = pluginType,
            ParametersSchema = parametersSchema,
            IconUrl = iconUrl,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string description, string pluginType, string parametersSchema, string iconUrl)
    {
        try { System.Text.Json.JsonDocument.Parse(parametersSchema); } catch { /* Handle invalid schema? */ }

        Name = name;
        Description = description;
        PluginType = pluginType;
        ParametersSchema = parametersSchema;
        IconUrl = iconUrl;
        UpdatedAt = DateTime.UtcNow;
    }
}