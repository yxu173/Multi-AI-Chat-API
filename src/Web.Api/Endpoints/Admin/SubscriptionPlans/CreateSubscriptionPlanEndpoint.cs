using Application.Features.Admin.SubscriptionPlans.CreateSubscriptionPlan;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.SubscriptionPlans;

public class CreateSubscriptionPlanEndpoint : Endpoint<CreateSubscriptionPlanRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/admin/subscription-plans");
    }

    public override async Task HandleAsync(CreateSubscriptionPlanRequest req, CancellationToken ct)
    {
        var command = new CreateSubscriptionPlanCommand(
            req.Name,
            req.Description,
            req.MaxRequestsPerMonth,
            req.MaxTokensPerRequest,
            req.MonthlyPrice
        );
        var result = await command.ExecuteAsync();
        if (result.IsSuccess)
        {
            await SendCreatedAtAsync(
                "GetSubscriptionPlanByIdEndpoint",
                new { id = result.Value },
                result.Value, cancellation: ct);
        }
        else
        {
            await SendErrorsAsync(400, ct);
        }
    }
}