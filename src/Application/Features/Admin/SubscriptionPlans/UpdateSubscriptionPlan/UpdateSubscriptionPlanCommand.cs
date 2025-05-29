using Application.Abstractions.Messaging;

namespace Application.Features.Admin.SubscriptionPlans.UpdateSubscriptionPlan;

public sealed record UpdateSubscriptionPlanCommand(
    Guid SubscriptionPlanId,
    string? Name = null,
    string? Description = null,
    int? MaxRequestsPerDay = null,
    int? MaxTokensPerRequest = null,
    decimal? MonthlyPrice = null,
    bool? IsActive = null) : ICommand<bool>;
