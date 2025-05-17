using Application.Services.Messaging;
using FastEndpoints;

namespace Application.Notifications;

public class MessageUpdateNotification : IEvent
{
    public Guid ChatSessionId { get; }
    public MessageDto Message { get; }

    public MessageUpdateNotification(Guid chatSessionId, MessageDto message)
    {
        ChatSessionId = chatSessionId;
        Message = message;
    }
} 