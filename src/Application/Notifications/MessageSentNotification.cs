using Application.Services;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using MediatR;

namespace Application.Notifications;

public class MessageSentNotification : INotification
{
    public Guid ChatSessionId { get; }
    public MessageDto Message { get; }

    public MessageSentNotification(Guid chatSessionId, MessageDto message)
    {
        ChatSessionId = chatSessionId;
        Message = message;
    }
}