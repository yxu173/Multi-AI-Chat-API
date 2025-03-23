using MediatR;

namespace Application.Notifications;

public class ResponseStoppedNotification : INotification
{
    public Guid ChatSessionId { get; }
    public Guid MessageId { get; }

    public ResponseStoppedNotification(Guid chatSessionId, Guid messageId)
    {
        ChatSessionId = chatSessionId;
        MessageId = messageId;
    }
}