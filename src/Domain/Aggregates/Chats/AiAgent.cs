using Domain.Common;

namespace Domain.Aggregates.Chats;

public sealed class AiAgent : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string SystemPrompt { get; private set; } = string.Empty;
    public Guid AiModelId { get; private set; }
    public string? IconUrl { get; private set; }
    
    private readonly List<AiAgentPlugin> _aiAgentPlugins = new();
    public IReadOnlyList<AiAgentPlugin> AiAgentPlugins => _aiAgentPlugins.AsReadOnly();
    
    public AiModel AiModel { get; private set; } = null!;

    private AiAgent()
    {
    }

    public static AiAgent Create(
        Guid userId, 
        string name, 
        string description, 
        string systemPrompt, 
        Guid aiModelId, 
        string? iconUrl = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(systemPrompt)) throw new ArgumentException("SystemPrompt cannot be empty.", nameof(systemPrompt));
        if (aiModelId == Guid.Empty) throw new ArgumentException("AiModelId cannot be empty.", nameof(aiModelId));

        return new AiAgent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Description = description,
            SystemPrompt = systemPrompt,
            AiModelId = aiModelId,
            IconUrl = iconUrl,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(
        string name, 
        string description, 
        string systemPrompt, 
        Guid aiModelId, 
        string? iconUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(systemPrompt)) throw new ArgumentException("SystemPrompt cannot be empty.", nameof(systemPrompt));
        if (aiModelId == Guid.Empty) throw new ArgumentException("AiModelId cannot be empty.", nameof(aiModelId));

        Name = name;
        Description = description;
        SystemPrompt = systemPrompt;
        AiModelId = aiModelId;
        IconUrl = iconUrl;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void AddPlugin(Guid pluginId, int order, bool isActive = true)
    {
        var aiAgentPlugin = AiAgentPlugin.Create(Id, pluginId, order, isActive);
        _aiAgentPlugins.Add(aiAgentPlugin);
    }

    public void RemovePlugin(Guid pluginId)
    {
        var plugin = _aiAgentPlugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin != null)
        {
            _aiAgentPlugins.Remove(plugin);
        }
    }

    public void UpdatePluginOrder(Guid pluginId, int newOrder)
    {
        var plugin = _aiAgentPlugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin != null)
        {
            plugin.UpdateOrder(newOrder);
        }
    }

    public void TogglePluginStatus(Guid pluginId, bool isActive)
    {
        var plugin = _aiAgentPlugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin != null)
        {
            plugin.SetActive(isActive);
        }
    }
}
