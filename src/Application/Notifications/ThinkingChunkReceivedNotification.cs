using MediatR;

namespace Application.Notifications;

public class ThinkingChunkReceivedNotification : INotification
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