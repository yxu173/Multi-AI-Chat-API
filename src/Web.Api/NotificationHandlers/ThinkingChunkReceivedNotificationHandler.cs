using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ThinkingChunkReceivedNotificationHandler : INotificationHandler<ThinkingChunkReceivedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ThinkingChunkReceivedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(ThinkingChunkReceivedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ReceiveThinkingChunk", notification.MessageId, notification.Chunk, cancellationToken);
    }
} 