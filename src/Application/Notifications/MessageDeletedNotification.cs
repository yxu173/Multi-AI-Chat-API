using FastEndpoints;

namespace Application.Notifications;

public record MessageDeletedNotification(Guid ChatSessionId, IReadOnlyList<Guid> MessagesId) : IEvent; 