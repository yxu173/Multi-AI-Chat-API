using Domain.Aggregates.Chats;
using Domain.Common;

public sealed class ChatSession : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public Guid AiModelId { get; private set; }
    public string? CustomApiKey { get; private set; }
    public Guid? FolderId { get; private set; }
    private readonly List<Message> _messages = new();
    private readonly List<ChatSessionPlugin> _chatSessionPlugins = new();
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();
    public IReadOnlyList<ChatSessionPlugin> ChatSessionPlugins => _chatSessionPlugins.AsReadOnly();

    public AiModel AiModel { get; private set; } = null!;
    public ChatFolder? Folder { get; private set; }

    private ChatSession()
    {
    }

    public static ChatSession Create(Guid userId, Guid aiModelId, string? customApiKey = null, Guid? folderId = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (aiModelId == Guid.Empty) throw new ArgumentException("AiModelId cannot be empty.", nameof(aiModelId));

        return new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "New Chat",
            AiModelId = aiModelId,
            CustomApiKey = customApiKey,
            FolderId = folderId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddMessage(Message message)
    {
        _messages.Add(message);
    }

    public void UpdateTitle(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
            throw new ArgumentException("Title cannot be empty.", nameof(newTitle));
        Title = newTitle;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void UpdateApiKey(string apiKey)
    {
        CustomApiKey = apiKey;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void AddPlugin(Guid pluginId, int order, bool isActive = true)
    {
        var chatSessionPlugin = ChatSessionPlugin.Create(Id, pluginId, order, isActive);
        _chatSessionPlugins.Add(chatSessionPlugin);
    }
    
    public void MoveToFolder(Guid? folderId)
    {
        FolderId = folderId;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void RemoveFromFolder()
    {
        FolderId = null;
        Folder = null;
        LastModifiedAt = DateTime.UtcNow;
    }
}