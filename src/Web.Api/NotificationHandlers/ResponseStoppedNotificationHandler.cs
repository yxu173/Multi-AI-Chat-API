using Application.Notifications;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ResponseStoppedNotificationHandler : IEventHandler<ResponseStoppedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ResponseStoppedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task HandleAsync(ResponseStoppedNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("ResponseStopped", eventModel.MessageId, ct);
    }
}