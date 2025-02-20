using Domain.Aggregates.Users;
using Domain.Common;
using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class ChatSession : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Title { get; private set; }
    public bool IsActive { get; private set; }
    public ModelType ModelType { get; private set; }

    private readonly List<Message> _messages = new();
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();

    // Navigation property
    public User User { get; private set; }

    private ChatSession()
    {
    }

    public static ChatSession Create(Guid userId, ModelType modelType)
    {
        return new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "New Chat",
            ModelType = modelType,
            IsActive = true
        };
    }

    public void AddMessage(Message message) => _messages.Add(message);
    public void UpdateTitle(string newTitle) => Title = newTitle.Trim();
    public void ToggleActive(bool active) => IsActive = active;
}