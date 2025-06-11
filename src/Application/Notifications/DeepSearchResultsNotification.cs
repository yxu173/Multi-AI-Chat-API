using FastEndpoints;

namespace Application.Notifications;

public record DeepSearchResultsNotification(Guid ChatSessionId, string Results) : IEvent; 