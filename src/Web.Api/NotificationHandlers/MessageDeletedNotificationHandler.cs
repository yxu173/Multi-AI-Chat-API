using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageDeletedNotificationHandler : INotificationHandler<MessageDeletedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageDeletedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(MessageDeletedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Handler: Deleting message {notification.MessageId} in chat {notification.ChatSessionId}");
        return _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("MessageDeleted", notification.MessageId, cancellationToken);
    }
} 