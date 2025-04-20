using MediatR;

namespace Application.Notifications;

public record ChatTitleUpdatedNotification(Guid ChatId, string NewTitle) : INotification;