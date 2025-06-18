using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class DeepSearchNotificationHandler :
    IEventHandler<DeepSearchStartedNotification>,
    IEventHandler<DeepSearchResultsNotification>,
    IEventHandler<DeepSearchErrorNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public DeepSearchNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task HandleAsync(DeepSearchStartedNotification notification, CancellationToken ct)
        => _hubContext.Clients.Group(notification.ChatSessionId.ToString()).SendAsync("DeepSearchStarted", notification.Message, ct);

    public Task HandleAsync(DeepSearchResultsNotification notification, CancellationToken ct)
        => _hubContext.Clients.Group(notification.ChatSessionId.ToString()).SendAsync("DeepSearchResults", notification.Results, ct);

    public Task HandleAsync(DeepSearchErrorNotification notification, CancellationToken ct)
        => _hubContext.Clients.Group(notification.ChatSessionId.ToString()).SendAsync("DeepSearchError", notification.ErrorMessage, ct);
} 