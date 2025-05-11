using Application.Notifications;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class TokenUsageUpdatedNotificationHandler : IEventHandler<TokenUsageUpdatedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public TokenUsageUpdatedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task HandleAsync(TokenUsageUpdatedNotification eventModel, CancellationToken ct)
    {
        await _hubContext.Clients.Group(eventModel.ChatSessionId.ToString())
            .SendAsync("TokenUsageUpdated", 
                new
                {
                    chatSessionId = eventModel.ChatSessionId,
                    inputTokens = eventModel.InputTokens,
                    outputTokens = eventModel.OutputTokens,
                    totalTokens = eventModel.InputTokens + eventModel.OutputTokens,
                    totalCost = Math.Round(eventModel.TotalCost, 4)
                }, 
                ct);
    }
}