using Domain.Common;

namespace Domain.Aggregates.Chats;

public class ChatSessionPlugin : BaseAuditableEntity
{
    public Guid ChatSessionId { get; private set; }
    public Guid PluginId { get; private set; }
    public bool IsActive { get; private set; }
    public int Order { get; private set; }


    public ChatSession ChatSession { get; private set; }
    public Plugin Plugin { get; private set; }

    private ChatSessionPlugin()
    {
    }

    public static ChatSessionPlugin Create(Guid chatSessionId, Guid pluginId, int order, bool isActive = true)
    {
        return new ChatSessionPlugin
        {
            Id = Guid.NewGuid(),
            ChatSessionId = chatSessionId,
            PluginId = pluginId,
            IsActive = isActive,
            Order = order,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public void UpdateLastUsed()
    {
        LastModifiedAt = DateTime.UtcNow;
    }
}