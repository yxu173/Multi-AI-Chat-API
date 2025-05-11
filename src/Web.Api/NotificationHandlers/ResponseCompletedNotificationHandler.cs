using Application.Notifications;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ResponseCompletedNotificationHandler : IEventHandler<ResponseCompletedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ResponseCompletedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task HandleAsync(ResponseCompletedNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("ResponseCompleted", eventModel.MessageId, ct);
    }
}