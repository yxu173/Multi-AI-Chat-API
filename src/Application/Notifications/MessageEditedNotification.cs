using MediatR;

namespace Application.Notifications;

public record MessageEditedNotification(Guid ChatSessionId, Guid MessageId, string NewContent) : INotification;