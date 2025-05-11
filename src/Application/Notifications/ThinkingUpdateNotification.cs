using FastEndpoints;
using MediatR;

namespace Application.Notifications;


public record ThinkingUpdateNotification : IEvent
{
   
    public Guid ChatSessionId { get; }

    public Guid MessageId { get; }

   
    public string ThinkingContent { get; }

    public ThinkingUpdateNotification(Guid chatSessionId, Guid messageId, string thinkingContent)
    {
        ChatSessionId = chatSessionId;
        MessageId = messageId;
        ThinkingContent = thinkingContent;
    }
} 