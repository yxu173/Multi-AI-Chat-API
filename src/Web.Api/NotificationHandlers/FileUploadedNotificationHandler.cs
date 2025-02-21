using Application.Notifications;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Web.Api.Hubs;

namespace Web.Api.NotificationHandlers;

public class FileUploadedNotificationHandler : INotificationHandler<FileUploadedNotification>
{
    private readonly IHubContext<ChatHub> _hubContext;

    public FileUploadedNotificationHandler(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task Handle(FileUploadedNotification notification, CancellationToken cancellationToken)
    {
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ReceiveFile", notification.FileAttachment, cancellationToken);
    }
}