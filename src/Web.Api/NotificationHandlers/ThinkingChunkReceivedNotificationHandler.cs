using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class ThinkingChunkReceivedNotificationHandler : IEventHandler<ThinkingChunkReceivedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ThinkingChunkReceivedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task HandleAsync(ThinkingChunkReceivedNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("ReceiveThinkingChunk", eventModel.MessageId, eventModel.Chunk, ct);
    }
} 