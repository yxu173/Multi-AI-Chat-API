using MediatR;

namespace Application.Notifications;

public record ResponseCompletedNotification(Guid ChatSessionId, Guid MessageId) : INotification;