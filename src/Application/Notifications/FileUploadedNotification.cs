using Domain.Aggregates.Chats;
using MediatR;

namespace Application.Notifications;

public class FileUploadedNotification : INotification
{
    public Guid ChatSessionId { get; }
    public FileAttachment FileAttachment { get; }

    public FileUploadedNotification(Guid chatSessionId, FileAttachment fileAttachment)
    {
        ChatSessionId = chatSessionId;
        FileAttachment = fileAttachment;
    }
}