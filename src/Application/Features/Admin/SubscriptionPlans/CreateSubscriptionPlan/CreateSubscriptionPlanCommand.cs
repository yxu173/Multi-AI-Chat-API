using Application.Abstractions.Messaging;

namespace Application.Features.Admin.SubscriptionPlans.CreateSubscriptionPlan;

public sealed record CreateSubscriptionPlanCommand(
    string Name,
    string Description,
    int MaxRequestsPerDay,
    int MaxTokensPerRequest,
    decimal MonthlyPrice) : ICommand<Guid>;
