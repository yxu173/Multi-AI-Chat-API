using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;

namespace Application.Features.Admin.UserSubscriptions.GetUserSubscriptions;

public sealed record GetUserSubscriptionsQuery(
    Guid? UserId = null,
    Guid? PlanId = null,
    bool ActiveOnly = false) : IQuery<IReadOnlyList<UserSubscription>>;
