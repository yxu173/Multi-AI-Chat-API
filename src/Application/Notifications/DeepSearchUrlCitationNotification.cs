using FastEndpoints;

namespace Application.Notifications;

public record DeepSearchUrlCitationNotification(Guid ChatSessionId, string Title, string Url) : IEvent; 