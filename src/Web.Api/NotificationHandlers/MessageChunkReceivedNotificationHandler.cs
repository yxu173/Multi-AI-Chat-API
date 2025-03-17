using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageChunkReceivedNotificationHandler : INotificationHandler<MessageChunkReceivedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageChunkReceivedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(MessageChunkReceivedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ReceiveMessageChunk", notification.MessageId, notification.Chunk, cancellationToken);
    }
}