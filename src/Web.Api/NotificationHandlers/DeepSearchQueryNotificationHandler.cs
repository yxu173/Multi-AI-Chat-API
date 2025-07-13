using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class DeepSearchQueryNotificationHandler : IEventHandler<DeepSearchQueryNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<DeepSearchQueryNotificationHandler> _logger;

    public DeepSearchQueryNotificationHandler(
        IHubContext<ChatHub> hubContext,
        ILogger<DeepSearchQueryNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task HandleAsync(DeepSearchQueryNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending deep search query to chat session {ChatSessionId}: {Query}", 
            notification.ChatSessionId, notification.Query);

        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("DeepSearchQuery", notification.ChatSessionId, notification.Query, cancellationToken);
    }
} 