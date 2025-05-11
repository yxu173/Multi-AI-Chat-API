using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageSentNotificationHandler : IEventHandler<MessageSentNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageSentNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task HandleAsync(MessageSentNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("ReceiveMessage", eventModel.Message, ct);
    }

}