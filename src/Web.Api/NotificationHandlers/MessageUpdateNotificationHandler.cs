using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;
using System.Threading;
using System.Threading.Tasks;

namespace Web.Api.NotificationHandlers;

public class MessageUpdateNotificationHandler : IEventHandler<MessageUpdateNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageUpdateNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task HandleAsync(MessageUpdateNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("MessageUpdated", eventModel.Message, ct);
    }
} 