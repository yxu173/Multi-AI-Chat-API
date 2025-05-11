using FastEndpoints;

namespace Application.Notifications;

public record MessageDeletedNotification(Guid ChatSessionId, Guid MessageId) : IEvent; 