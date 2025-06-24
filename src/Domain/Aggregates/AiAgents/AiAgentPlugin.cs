using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using Domain.Common;

namespace Domain.Aggregates.AiAgents;

public sealed class AiAgentPlugin : BaseEntity
{
    public Guid AiAgentId { get; private set; }
    public Guid PluginId { get; private set; }
    public bool IsActive { get; private set; }

    public AiAgent AiAgent { get; private set; } = null!;
    public Plugin Plugin { get; private set; } = null!;

    private AiAgentPlugin()
    {
    }

    public static AiAgentPlugin Create(Guid aiAgentId, Guid pluginId, bool isActive = true)
    {
        if (aiAgentId == Guid.Empty) throw new ArgumentException("AiAgentId cannot be empty.", nameof(aiAgentId));
        if (pluginId == Guid.Empty) throw new ArgumentException("PluginId cannot be empty.", nameof(pluginId));

        return new AiAgentPlugin
        {
            Id = Guid.NewGuid(),
            AiAgentId = aiAgentId,
            PluginId = pluginId,
            IsActive = isActive
        };
    }


    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}