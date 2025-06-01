using Application.Abstractions.Messaging;

namespace Application.Features.Admin.SubscriptionPlans.CreateSubscriptionPlan;

public sealed record CreateSubscriptionPlanCommand(
    string Name,
    string Description,
    double MaxRequestsPerMonth,
    int MaxTokensPerRequest,
    decimal MonthlyPrice) : ICommand<Guid>;
