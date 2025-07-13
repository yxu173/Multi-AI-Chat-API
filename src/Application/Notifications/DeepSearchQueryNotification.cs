using FastEndpoints;

namespace Application.Notifications;

public record DeepSearchQueryNotification(Guid ChatSessionId, string Query) : IEvent; 