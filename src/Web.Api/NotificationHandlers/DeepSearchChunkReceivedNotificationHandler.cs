using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class DeepSearchChunkReceivedNotificationHandler : IEventHandler<DeepSearchChunkReceivedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public DeepSearchChunkReceivedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task HandleAsync(DeepSearchChunkReceivedNotification notification, CancellationToken ct)
        => _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("DeepSearchChunk", notification.Chunk, ct);
} 