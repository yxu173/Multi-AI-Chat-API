using Domain.Common;
using Domain.ValueObjects;
using System.Text.Json;
using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class AiAgent : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string? IconUrl { get; private set; }

    public List<AgentCategories> Categories { get; private set; } = new();
    public bool AssignCustomModelParameters { get; private set; }
    
    public ModelParameters? ModelParameter { get; private set; }
    
    public string? ProfilePictureUrl { get; private set; }

    private readonly List<AiAgentPlugin> _aiAgentPlugins = new();
    public IReadOnlyList<AiAgentPlugin> AiAgentPlugins => _aiAgentPlugins.AsReadOnly();

    public Guid AiModelId { get; private set; }
    public AiModel? AiModel { get; private set; }

    private AiAgent()
    {
    }

  public static AiAgent Create(
        Guid userId,
        string name,
        string description,
        string? iconUrl = null,
        List<string>? categories = null,
        bool assignCustomModelParameters = false,
        ModelParameters? modelParameters = null,
        string? profilePictureUrl = null,
        Guid? aiModelId = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));

        var agent = new AiAgent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Description = description,
            IconUrl = iconUrl,
            Categories = categories?.Select(c => Enum.TryParse<AgentCategories>(c, true, out var cat) ? cat : default).Where(c => c != default).ToList() ?? new List<AgentCategories>(),
            AssignCustomModelParameters = assignCustomModelParameters,
            ProfilePictureUrl = profilePictureUrl,
            ModelParameter = ModelParameters.Create(),
            CreatedAt = DateTime.UtcNow
        };

        if (assignCustomModelParameters && modelParameters != null)
        {
            agent.ModelParameter = modelParameters;
        }

        if (aiModelId.HasValue && aiModelId.Value != Guid.Empty)
        {
            agent.SetAiModelId(aiModelId.Value);
        }

        return agent;
    }

    public void Update(
        string name,
        string description,
        string? iconUrl = null,
        List<string>? categories = null,
        bool? assignCustomModelParameters = null,
        ModelParameters? modelParameters = null,
        string? profilePictureUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));

        Name = name;
        Description = description;
        IconUrl = iconUrl;

        if (categories != null) Categories = categories.Select(c => Enum.TryParse<AgentCategories>(c, true, out var cat) ? cat : default).Where(c => c != default).ToList();
        if (assignCustomModelParameters.HasValue) AssignCustomModelParameters = assignCustomModelParameters.Value;
        if (modelParameters != null && AssignCustomModelParameters) ModelParameter = modelParameters;
        if (profilePictureUrl != null) ProfilePictureUrl = profilePictureUrl;

        LastModifiedAt = DateTime.UtcNow;
    }
   
    
    public void AddCategory(string categoryName)
    {
        if (Enum.TryParse<AgentCategories>(categoryName, true, out var category) && !Categories.Contains(category))
        {
            Categories.Add(category);
        }
    }

    public void RemoveCategory(AgentCategories category)
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
    
    public void AddPlugin(Guid pluginId, bool isActive = true)
    {
        var aiAgentPlugin = AiAgentPlugin.Create(Id, pluginId, isActive);
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

    public void TogglePluginStatus(Guid pluginId, bool isActive)
    {
        var plugin = _aiAgentPlugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin != null)
        {
            plugin.SetActive(isActive);
        }
    }

    public void SetAiModelId(Guid aiModelId)
    {
        // Store just the ID; the actual AiModel should be retrieved from repository when needed
        AiModelId = aiModelId;
        // Note: AiModel property will be set by the repository or mapper when loading the full entity
    }
}