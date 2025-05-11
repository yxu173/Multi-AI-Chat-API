using FastEndpoints;

namespace Application.Notifications;

public class ThinkingChunkReceivedNotification : IEvent
{
    public Guid ChatSessionId { get; }
    public Guid MessageId { get; }
    public string Chunk { get; }

    public ThinkingChunkReceivedNotification(Guid chatSessionId, Guid messageId, string chunk)
    {
        ChatSessionId = chatSessionId;
        MessageId = messageId;
        Chunk = chunk;
    }
} 