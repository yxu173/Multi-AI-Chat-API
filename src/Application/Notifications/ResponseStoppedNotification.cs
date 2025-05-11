using FastEndpoints;

namespace Application.Notifications;

public class ResponseStoppedNotification : IEvent
{
    public Guid ChatSessionId { get; }
    public Guid MessageId { get; }

    public ResponseStoppedNotification(Guid chatSessionId, Guid messageId)
    {
        ChatSessionId = chatSessionId;
        MessageId = messageId;
    }
}