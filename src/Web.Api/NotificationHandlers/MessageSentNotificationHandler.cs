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
        var message = notification.Message;
        
        // Create a simplified object for the client to avoid serialization issues
        var messageInfo = new
        {
            Content = message.Content,
            IsFromAi = message.IsFromAi,
            MessageId = message.MessageId,
            FileAttachments = message.FileAttachments?.Select(fa => new
            {
                Id = fa.Id,
                MessageId = fa.MessageId,
                FileName = fa.FileName,
                ContentType = fa.ContentType,
                FileType = fa.FileType.ToString(),
                Size = fa.FileSize,
                HasBase64 = fa.Base64Content != null,
                Url = $"/api/Files/{fa.Id}"
            }).ToList()
        };
        
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ReceiveMessage", messageInfo, cancellationToken);
    }
}