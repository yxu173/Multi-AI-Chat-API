using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Features.Admin.SubscriptionPlans;
using Application.Features.Admin.SubscriptionPlans.GetSubscriptionPlans;
using Domain.Aggregates.Admin;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.SubscriptionPlans;

public class GetSubscriptionPlansEndpoint : EndpointWithoutRequest<List<SubscriptionPlanResponse>>
{
    public override void Configure()
    {
        Get("/api/admin/subscription-plans");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        bool activeOnly = false;
        if (HttpContext.Request.Query.TryGetValue("activeOnly", out var activeOnlyStr))
            bool.TryParse(activeOnlyStr, out activeOnly);

        var result = await new GetSubscriptionPlansQuery(activeOnly).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
        {
            var responses = result.Value.Select(ToSubscriptionPlanResponse).ToList();
            await SendAsync(responses, cancellation: ct);
        }
        else
        {
            await SendErrorsAsync(400, ct);
        }
    }

    private static SubscriptionPlanResponse ToSubscriptionPlanResponse(SubscriptionPlan plan)
        => new(
            plan.Id,
            plan.Name,
            plan.Description,
            plan.MaxRequestsPerMonth,
            plan.MaxTokensPerRequest,
            plan.MonthlyPrice,
            plan.IsActive,
            plan.CreatedAt,
            plan.LastModified
        );
}