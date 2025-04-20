using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageEditedNotificationHandler : INotificationHandler<MessageEditedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageEditedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(MessageEditedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("MessageEdited", notification.MessageId, notification.NewContent, cancellationToken);
    }
}