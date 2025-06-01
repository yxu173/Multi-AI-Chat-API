using System;

namespace Web.Api.Contracts.Admin;

public record UserSubscriptionResponse(
    Guid Id,
    Guid UserId,
    Guid SubscriptionPlanId,
    string SubscriptionPlanName,
    DateTime StartDate,
    DateTime ExpiryDate,
    double CurrentMonthUsage,
    bool IsActive,
    bool IsExpired,
    string? PaymentReference
);

public record AssignSubscriptionRequest(
    Guid UserId,
    Guid SubscriptionPlanId,
    DateTime? StartDate = null,
    int DurationMonths = 1,
    string? PaymentReference = null
);

public record CancelSubscriptionRequest(
    Guid SubscriptionId
);
