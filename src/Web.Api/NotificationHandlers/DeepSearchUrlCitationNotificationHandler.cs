using Application.Notifications;
using FastEndpoints;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class DeepSearchUrlCitationNotificationHandler : IEventHandler<DeepSearchUrlCitationNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<DeepSearchUrlCitationNotificationHandler> _logger;

    public DeepSearchUrlCitationNotificationHandler(
        IHubContext<ChatHub> hubContext,
        ILogger<DeepSearchUrlCitationNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task HandleAsync(DeepSearchUrlCitationNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending deep search URL citation to chat session {ChatSessionId}: {Title} - {Url}", 
            notification.ChatSessionId, notification.Title, notification.Url);

        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("DeepSearchUrlCitation", notification.ChatSessionId, notification.Title, notification.Url, cancellationToken);
    }
} 