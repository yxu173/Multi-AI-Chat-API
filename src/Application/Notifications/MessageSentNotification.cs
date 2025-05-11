using Application.Services.Messaging;
using FastEndpoints;

namespace Application.Notifications;

public class MessageSentNotification : IEvent
{
    public Guid ChatSessionId { get; }
    public MessageDto Message { get; }

    public MessageSentNotification(Guid chatSessionId, MessageDto message)
    {
        ChatSessionId = chatSessionId;
        Message = message;
    }
}