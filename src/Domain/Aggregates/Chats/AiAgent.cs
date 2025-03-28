using Domain.Common;
using Domain.ValueObjects;
using System.Text.Json;

namespace Domain.Aggregates.Chats;

public sealed class AiAgent : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string? SystemInstructions { get; private set; }
    public Guid AiModelId { get; private set; }
    public string? IconUrl { get; private set; }

    public List<string> Categories { get; private set; } = new();
    public bool AssignCustomModelParameters { get; private set; }
    
    // Owned entity for ModelParameters
    public ModelParameters? ModelParameter { get; private set; }
    
    public string? ProfilePictureUrl { get; private set; }

    // Plugins related properties
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
        Guid aiModelId,
        string? systemInstructions = null,
        string? iconUrl = null,
        List<string>? categories = null,
        bool assignCustomModelParameters = false,
        ModelParameters? modelParameters = null,
        string? profilePictureUrl = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (aiModelId == Guid.Empty) throw new ArgumentException("AiModelId cannot be empty.", nameof(aiModelId));

        var agent = new AiAgent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            SystemInstructions = systemInstructions,
            Description = description,
            AiModelId = aiModelId,
            IconUrl = iconUrl,
            Categories = categories ?? new List<string>(),
            AssignCustomModelParameters = assignCustomModelParameters,
            ProfilePictureUrl = profilePictureUrl,
            CreatedAt = DateTime.UtcNow
        };

        if (assignCustomModelParameters && modelParameters != null)
        {
            agent.ModelParameter = modelParameters;
        }

        return agent;
    }

    public void Update(
        string name,
        string description,
        Guid aiModelId,
        string? systemInstructions = null,
        string? iconUrl = null,
        List<string>? categories = null,
        bool? assignCustomModelParameters = null,
        ModelParameters? modelParameters = null,
        string? profilePictureUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (aiModelId == Guid.Empty) throw new ArgumentException("AiModelId cannot be empty.", nameof(aiModelId));

        Name = name;
        Description = description;
        AiModelId = aiModelId;
        IconUrl = iconUrl;

        if (systemInstructions != null) SystemInstructions = systemInstructions;
        if (categories != null) Categories = categories;
        if (assignCustomModelParameters.HasValue) AssignCustomModelParameters = assignCustomModelParameters.Value;
        if (modelParameters != null && AssignCustomModelParameters) ModelParameter = modelParameters;
        if (profilePictureUrl != null) ProfilePictureUrl = profilePictureUrl;

        LastModifiedAt = DateTime.UtcNow;
    }
    
    public void AddCategory(string category)
    {
        if (!string.IsNullOrWhiteSpace(category) && !Categories.Contains(category))
        {
            Categories.Add(category);
        }
    }

    public void RemoveCategory(string category)
    {
        Categories.Remove(category);
    }

    public void ClearCategories()
    {
        Categories.Clear();
    }
    
    public void SetCustomModelParameters(bool enabled, ModelParameters? parameters = null)
    {
        AssignCustomModelParameters = enabled;
        if (enabled && parameters != null)
        {
            ModelParameter = parameters;
        }
        else if (!enabled)
        {
            ModelParameter = null;
        }
    }

    public ModelParameters? GetModelParameters()
    {
        if (!AssignCustomModelParameters)
            return null;
            
        return ModelParameter;
    }

    public void SetSystemInstructions(string instructions)
    {
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            SystemInstructions = instructions;
        }
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