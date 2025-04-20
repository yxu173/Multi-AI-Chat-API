using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ChatTitleUpdatedNotificationHandler : INotificationHandler<ChatTitleUpdatedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatTitleUpdatedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(ChatTitleUpdatedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.All.SendAsync(
            "ChatTitleUpdated",
            notification.ChatId,
            notification.NewTitle,
            cancellationToken);
    }
}