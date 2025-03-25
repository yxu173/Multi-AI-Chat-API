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
        var fileAttachment = notification.FileAttachment;
        
        // Create a simplified object for the client to avoid serialization issues
        var fileInfo = new
        {
            Id = fileAttachment.Id,
            MessageId = fileAttachment.MessageId,
            FileName = fileAttachment.FileName,
            ContentType = fileAttachment.ContentType,
            FileType = fileAttachment.FileType.ToString(),
            Size = fileAttachment.FileSize,
            HasBase64 = fileAttachment.Base64Content != null,
            Url = $"/api/Files/{fileAttachment.Id}"
        };
        
        await _hubContext.Clients.Group(notification.ChatSessionId.ToString())
            .SendAsync("ReceiveFile", fileInfo, cancellationToken);
    }
}