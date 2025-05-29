using Application.Abstractions.Messaging;

namespace Application.Features.Admin.UserSubscriptions.AssignSubscriptionToUser;

public sealed record AssignSubscriptionToUserCommand(
    Guid UserId,
    Guid SubscriptionPlanId,
    DateTime? StartDate = null,
    int DurationMonths = 1,
    string? PaymentReference = null) : ICommand<Guid>;
