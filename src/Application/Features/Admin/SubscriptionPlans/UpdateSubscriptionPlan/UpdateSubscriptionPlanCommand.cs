using Application.Abstractions.Messaging;

namespace Application.Features.Admin.SubscriptionPlans.UpdateSubscriptionPlan;

public sealed record UpdateSubscriptionPlanCommand(
    Guid SubscriptionPlanId,
    string? Name = null,
    string? Description = null,
    double? MaxRequestsPerMonth = null,
    int? MaxTokensPerRequest = null,
    decimal? MonthlyPrice = null,
    bool? IsActive = null) : ICommand<bool>;
