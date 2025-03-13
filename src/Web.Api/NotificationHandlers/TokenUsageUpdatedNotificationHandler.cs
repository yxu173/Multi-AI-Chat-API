using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class TokenUsageUpdatedNotificationHandler : INotificationHandler<TokenUsageUpdatedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public TokenUsageUpdatedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Handle(TokenUsageUpdatedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("TokenUsageUpdated", 
                new
                {
                    chatSessionId = notification.ChatSessionId,
                    inputTokens = notification.InputTokens,
                    outputTokens = notification.OutputTokens,
                    totalTokens = notification.InputTokens + notification.OutputTokens,
                    totalCost = Math.Round(notification.TotalCost, 4)
                }, 
                cancellationToken);
    }
}