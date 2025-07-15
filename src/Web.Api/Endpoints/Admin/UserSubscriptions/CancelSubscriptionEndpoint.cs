using System.Threading;
using System.Threading.Tasks;
using Application.Features.Admin.UserSubscriptions.CancelUserSubscription;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.UserSubscriptions;

public class CancelSubscriptionEndpoint : Endpoint<CancelSubscriptionRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/admin/user-subscriptions/cancel");
    }

    public override async Task HandleAsync(CancelSubscriptionRequest req, CancellationToken ct)
    {
        var command = new CancelUserSubscriptionCommand(req.SubscriptionId);
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