using FastEndpoints;

namespace Application.Notifications;

public record DeepSearchStartedNotification(Guid ChatSessionId, string Message) : IEvent; 