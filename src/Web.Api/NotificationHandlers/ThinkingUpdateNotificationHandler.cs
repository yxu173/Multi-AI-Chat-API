using Application.Notifications;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ThinkingUpdateNotificationHandler : IEventHandler<ThinkingUpdateNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ThinkingUpdateNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task HandleAsync(ThinkingUpdateNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients
            .Group(eventModel.ChatSessionId.ToString())
            .SendAsync("ThinkingUpdate", eventModel.MessageId, eventModel.ThinkingContent, ct);
    }
} 