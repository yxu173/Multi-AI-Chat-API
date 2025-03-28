using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageSentNotificationHandler : INotificationHandler<MessageSentNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageSentNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(MessageSentNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ReceiveMessage", notification.Message, cancellationToken);
    }
}