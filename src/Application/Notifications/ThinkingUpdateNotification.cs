using MediatR;

namespace Application.Notifications;

/// <summary>
/// Notification sent when an AI model provides a thinking update during stream processing
/// </summary>
public record ThinkingUpdateNotification : INotification
{
    /// <summary>
    /// The ID of the chat session this update belongs to
    /// </summary>
    public Guid ChatSessionId { get; }

    /// <summary>
    /// The ID of the message this update belongs to
    /// </summary>
    public Guid MessageId { get; }

    /// <summary>
    /// The thinking update content
    /// </summary>
    public string ThinkingContent { get; }

    public ThinkingUpdateNotification(Guid chatSessionId, Guid messageId, string thinkingContent)
    {
        ChatSessionId = chatSessionId;
        MessageId = messageId;
        ThinkingContent = thinkingContent;
    }
} 