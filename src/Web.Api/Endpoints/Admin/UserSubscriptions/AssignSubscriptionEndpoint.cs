using System.Threading;
using System.Threading.Tasks;
using Application.Features.Admin.UserSubscriptions.AssignSubscriptionToUser;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.UserSubscriptions;

public class AssignSubscriptionEndpoint : Endpoint<AssignSubscriptionRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/admin/user-subscriptions/assign");
    }

    public override async Task HandleAsync(AssignSubscriptionRequest req, CancellationToken ct)
    {
        var command = new AssignSubscriptionToUserCommand(
            req.UserId,
            req.SubscriptionPlanId,
            req.StartDate,
            req.DurationMonths,
            req.PaymentReference
        );
        var result = await command.ExecuteAsync(ct: ct);
        if (result.IsSuccess)
        {
            await SendCreatedAtAsync(
                "GetUserActiveSubscriptionEndpoint",
                new { userId = req.UserId },
                result.Value, cancellation: ct);
        }
        else
        {
            await SendErrorsAsync(400, ct);
        }
    }
} 