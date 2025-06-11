using FastEndpoints;

namespace Application.Notifications;

public record DeepSearchErrorNotification(Guid ChatSessionId, string ErrorMessage) : IEvent; 