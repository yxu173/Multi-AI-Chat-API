using Domain.Common;

namespace Domain.Aggregates.Chats;

public sealed class ChatFolder : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    
    private readonly List<ChatSession> _chatSessions = new();
    public IReadOnlyList<ChatSession> ChatSessions => _chatSessions.AsReadOnly();

    private ChatFolder()
    {
    }

    public static ChatFolder Create(Guid userId, string name, string? description = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));

        return new ChatFolder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateDetails(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        
        Name = name;
        Description = description;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void AddChatSession(ChatSession chatSession)
    {
        if (chatSession == null) throw new ArgumentNullException(nameof(chatSession));
        if (chatSession.UserId != UserId)
            throw new InvalidOperationException("Cannot add chat session from a different user.");

        if (!_chatSessions.Contains(chatSession))
        {
            _chatSessions.Add(chatSession);
        }
    }

    public void RemoveChatSession(ChatSession chatSession)
    {
        _chatSessions.Remove(chatSession);
    }
}
