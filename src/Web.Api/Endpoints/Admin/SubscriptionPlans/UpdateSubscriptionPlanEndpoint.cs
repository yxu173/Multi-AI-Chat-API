using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Features.Admin.SubscriptionPlans.UpdateSubscriptionPlan;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.SubscriptionPlans;

public class UpdateSubscriptionPlanEndpoint : Endpoint<UpdateSubscriptionPlanRequest, EmptyResponse>
{
    public override void Configure()
    {
        Put("/api/admin/subscription-plans/{id:guid}"); 
    }

    public override async Task HandleAsync(UpdateSubscriptionPlanRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = new UpdateSubscriptionPlanCommand(
            id,
            req.Name,
            req.Description,
            req.MaxRequestsPerMonth,
            req.MaxTokensPerRequest,
            req.MonthlyPrice,
            req.IsActive
        );
        var result = await command.ExecuteAsync(ct: ct);
        if (result.IsSuccess)
        {
            await SendNoContentAsync(ct);
        }
        else
        {
            await SendErrorsAsync(400, ct);
        }
    }
} 