using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageEditedNotificationHandler : IEventHandler<MessageEditedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageEditedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task HandleAsync(MessageEditedNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("MessageEdited", eventModel.MessageId, eventModel.NewContent, ct);
    }
}