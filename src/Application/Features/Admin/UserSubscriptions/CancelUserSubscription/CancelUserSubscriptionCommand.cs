using Application.Abstractions.Messaging;

namespace Application.Features.Admin.UserSubscriptions.CancelUserSubscription;

public sealed record CancelUserSubscriptionCommand(
    Guid SubscriptionId) : ICommand<bool>;
