using System;

namespace Web.Api.Contracts.Admin;

public record SubscriptionPlanResponse(
    Guid Id,
    string Name,
    string Description,
    double MaxRequestsPerMonth,
    int MaxTokensPerRequest,
    decimal MonthlyPrice,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastModified
);

public record CreateSubscriptionPlanRequest(
    string Name,
    string Description,
    double MaxRequestsPerMonth,
    int MaxTokensPerRequest,
    decimal MonthlyPrice
);

public record UpdateSubscriptionPlanRequest(
    string? Name = null,
    string? Description = null,
    double? MaxRequestsPerMonth = null,
    int? MaxTokensPerRequest = null,
    decimal? MonthlyPrice = null,
    bool? IsActive = null
);
