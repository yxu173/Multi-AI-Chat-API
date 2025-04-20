using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ResponseStoppedNotificationHandler : INotificationHandler<ResponseStoppedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ResponseStoppedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(ResponseStoppedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ResponseStopped", notification.MessageId, cancellationToken);
    }
}