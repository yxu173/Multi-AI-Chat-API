using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ResponseCompletedNotificationHandler : INotificationHandler<ResponseCompletedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ResponseCompletedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Handle(ResponseCompletedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ResponseCompleted", notification.MessageId, cancellationToken);
    }
}