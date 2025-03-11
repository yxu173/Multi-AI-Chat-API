 using Domain.Common;

namespace Domain.Aggregates.Users;

public class UserPluginPreference : BaseEntity
{
    public Guid UserId { get; private set; }
    public string PluginId { get; private set; }
    public bool IsEnabled { get; private set; }

    private UserPluginPreference() { }

    public static UserPluginPreference Create(Guid userId, string pluginId, bool isEnabled = false)
    {
        return new UserPluginPreference
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