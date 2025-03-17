using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class MessageSentNotificationHandler : INotificationHandler<MessageSentNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public MessageSentNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(MessageSentNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ReceiveMessage", notification.Message, cancellationToken);

        var searchTerms = ExtractSearchTerms(notification.Message.Content);
        foreach (var term in searchTerms)
        {
            await _hubContext.Clients.Group($"search:{term.ToLower()}")
                .SendAsync("SearchUpdate", new
                {
                    chatSessionId = notification.ChatSessionId,
                    messageId = notification.Message.MessageId,
                    content = notification.Message.Content,
                    timestamp = DateTime.UtcNow
                }, cancellationToken);
        }
    }

    private static IEnumerable<string> ExtractSearchTerms(string content)
    {
        return content.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?' }, 
                StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 3)
            .Distinct();
    }
}