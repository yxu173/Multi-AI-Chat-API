using MediatR;

namespace Application.Notifications;

public record MessageDeletedNotification(Guid ChatSessionId, Guid MessageId) : INotification; 