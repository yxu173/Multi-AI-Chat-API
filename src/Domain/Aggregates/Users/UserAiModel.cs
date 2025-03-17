using Domain.Aggregates.Chats;
using Domain.Common;

namespace Domain.Aggregates.Users;

public sealed class UserAiModel : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid AiModelId { get; set; }
    public bool IsEnabled { get; set; }

    public AiModel AiModel { get; set; } = null!;
    public User User { get; set; } = null!;

    private UserAiModel()
    {
    }

    public static UserAiModel Create(Guid userId, Guid aiModelId, bool isEnabled = true)
    {
        return new UserAiModel
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AiModelId = aiModelId,
            IsEnabled = isEnabled
        };
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }
}