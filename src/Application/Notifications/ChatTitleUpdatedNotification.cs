using FastEndpoints;

namespace Application.Notifications;

public record ChatTitleUpdatedNotification(Guid ChatId, string NewTitle) : IEvent;