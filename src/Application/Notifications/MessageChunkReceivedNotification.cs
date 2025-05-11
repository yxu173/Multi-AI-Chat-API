using FastEndpoints;
using MediatR;

namespace Application.Notifications;

public class MessageChunkReceivedNotification : IEvent
{
    public Guid ChatSessionId { get; }
    public Guid MessageId { get; }
    public string Chunk { get; }

    public MessageChunkReceivedNotification(Guid chatSessionId, Guid messageId, string chunk)
    {
        ChatSessionId = chatSessionId;
        MessageId = messageId;
        Chunk = chunk;
    }
}