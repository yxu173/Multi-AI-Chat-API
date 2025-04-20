using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ThinkingUpdateNotificationHandler : INotificationHandler<ThinkingUpdateNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ThinkingUpdateNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Handle(ThinkingUpdateNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(notification.ChatSessionId.ToString())
            .SendAsync("ThinkingUpdate", notification.MessageId, notification.ThinkingContent, cancellationToken);
    }
} 