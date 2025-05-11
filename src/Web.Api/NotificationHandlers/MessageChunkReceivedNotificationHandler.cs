using Application.Notifications;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageChunkReceivedNotificationHandler : IEventHandler<MessageChunkReceivedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageChunkReceivedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task HandleAsync(MessageChunkReceivedNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("ReceiveMessageChunk", eventModel.MessageId, eventModel.Chunk, ct);
    }

}