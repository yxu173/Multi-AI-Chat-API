using Domain.Common;

namespace Domain.Aggregates.Chats;

public sealed class SharedChat : BaseAuditableEntity
{
    public Guid ChatId { get; private set; }
    public Guid OwnerId { get; private set; }
    public string ShareToken { get; private set; } = string.Empty;
    public DateTime? ExpiresAt { get; private init; }
    public bool IsActive { get; private set; }

    public ChatSession Chat { get; private set; } = null!;

    private SharedChat()
    {
    }

    public static SharedChat Create(Guid chatId, Guid ownerId, DateTime? expiresAt = null)
    {
        if (chatId == Guid.Empty) throw new ArgumentException("ChatId cannot be empty.", nameof(chatId));
        if (ownerId == Guid.Empty) throw new ArgumentException("OwnerId cannot be empty.", nameof(ownerId));

        return new SharedChat
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            OwnerId = ownerId,
            ShareToken = Guid.NewGuid().ToString("N"),
            ExpiresAt = expiresAt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Deactivate()
    {
        IsActive = false;
        LastModifiedAt = DateTime.UtcNow;
    }

    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }
} 