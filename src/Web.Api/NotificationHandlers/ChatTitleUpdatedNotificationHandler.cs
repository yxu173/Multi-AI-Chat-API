using Application.Notifications;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ChatTitleUpdatedNotificationHandler : IEventHandler<ChatTitleUpdatedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatTitleUpdatedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task HandleAsync(ChatTitleUpdatedNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.All.SendAsync(
            "ChatTitleUpdated",
            eventModel.ChatId,
            eventModel.NewTitle,
            ct);
    }

}