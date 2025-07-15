using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Features.Admin.UserSubscriptions;
using Application.Features.Admin.UserSubscriptions.GetUserSubscriptions;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.UserSubscriptions;

public class GetUserSubscriptionsEndpoint : EndpointWithoutRequest<List<UserSubscriptionResponse>>
{
    public override void Configure()
    {
        Get("/api/admin/user-subscriptions");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Guid? userId = null;
        Guid? planId = null;
        bool activeOnly = false;
        if (HttpContext.Request.Query.TryGetValue("userId", out var userIdStr) &&
            Guid.TryParse(userIdStr, out var parsedUserId))
            userId = parsedUserId;
        if (HttpContext.Request.Query.TryGetValue("planId", out var planIdStr) &&
            Guid.TryParse(planIdStr, out var parsedPlanId))
            planId = parsedPlanId;
        if (HttpContext.Request.Query.TryGetValue("activeOnly", out var activeOnlyStr))
            bool.TryParse(activeOnlyStr, out activeOnly);

        var result = await new GetUserSubscriptionsQuery(userId, planId, activeOnly).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
        {
            var responses = result.Value.Select(ToUserSubscriptionResponse).ToList();
            await SendAsync(responses, cancellation: ct);
        }
        else
        {
            await SendErrorsAsync(400, ct);
        }
    }

    private static UserSubscriptionResponse ToUserSubscriptionResponse(
        Domain.Aggregates.Admin.UserSubscription subscription)
    {
        return new UserSubscriptionResponse(
            subscription.Id,
            subscription.UserId,
            subscription.SubscriptionPlanId,
            subscription.SubscriptionPlan.Name,
            subscription.StartDate,
            subscription.ExpiryDate,
            subscription.CurrentMonthUsage,
            subscription.IsActive,
            subscription.IsExpired(),
            subscription.PaymentReference
        );
    }
}