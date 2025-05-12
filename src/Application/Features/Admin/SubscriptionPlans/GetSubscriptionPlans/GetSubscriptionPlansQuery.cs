using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;

namespace Application.Features.Admin.SubscriptionPlans.GetSubscriptionPlans;

public sealed record GetSubscriptionPlansQuery(
    bool ActiveOnly = false) : IQuery<IReadOnlyList<SubscriptionPlan>>;
