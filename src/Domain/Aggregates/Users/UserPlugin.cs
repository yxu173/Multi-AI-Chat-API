using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using Domain.Common;

namespace Domain.Aggregates.Users;

public class UserPlugin : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid PluginId { get; private set; }
    public bool IsEnabled { get; private set; }
    
    public User User { get; private set; }
    public Plugin Plugin { get; private set; }
    private UserPlugin() { }

    public static UserPlugin Create(Guid userId, Guid pluginId, bool isEnabled = true)
    {
        return new UserPlugin
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PluginId = pluginId,
            IsEnabled = isEnabled
        };
    }

    public void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }
}