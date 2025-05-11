using Application.Services;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using FastEndpoints;
using MediatR;

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