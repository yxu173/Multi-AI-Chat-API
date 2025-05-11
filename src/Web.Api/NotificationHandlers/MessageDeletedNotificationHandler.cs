using Application.Notifications;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageDeletedNotificationHandler : IEventHandler<MessageDeletedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageDeletedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task HandleAsync(MessageDeletedNotification eventModel, CancellationToken ct)
    {
        Console.WriteLine($"Handler: Deleting message {eventModel.MessageId} in chat {eventModel.ChatSessionId}");
        return _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("MessageDeleted", eventModel.MessageId, ct);
    }
}