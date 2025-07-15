using Application.Features.Admin.SubscriptionPlans.GetSubscriptionPlans;
using Domain.Aggregates.Admin;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.SubscriptionPlans;

public class GetSubscriptionPlanByIdEndpoint : Endpoint<GetSubscriptionPlanByIdRequest, SubscriptionPlanResponse>
{
    public override void Configure()
    {
        Get("/api/admin/subscription-plans/{id:guid}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetSubscriptionPlanByIdRequest req, CancellationToken ct)
    {
        var result = await new GetSubscriptionPlansQuery().ExecuteAsync(ct: ct);
        if (result.IsSuccess)
        {
            var plan = result.Value.FirstOrDefault(p => p.Id == req.Id);
            if (plan == null)
            {
                await SendNotFoundAsync(ct);
                return;
            }

            await SendAsync(ToSubscriptionPlanResponse(plan), cancellation: ct);
        }
        else
        {
            await SendErrorsAsync(400);
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

public class GetSubscriptionPlanByIdRequest
{
    public Guid Id { get; set; }
}