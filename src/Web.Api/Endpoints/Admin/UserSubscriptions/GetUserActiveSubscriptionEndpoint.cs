using System;
using System.Threading;
using System.Threading.Tasks;
using Domain.Repositories;
using FastEndpoints;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.UserSubscriptions;

public class GetUserActiveSubscriptionEndpoint : Endpoint<GetUserActiveSubscriptionRequest, UserSubscriptionResponse>
{
    private readonly IUserSubscriptionRepository _userSubscriptionRepository;
    private readonly ISubscriptionPlanRepository _subscriptionPlanRepository;

    public GetUserActiveSubscriptionEndpoint(IUserSubscriptionRepository userSubscriptionRepository, ISubscriptionPlanRepository subscriptionPlanRepository)
    {
        _userSubscriptionRepository = userSubscriptionRepository;
        _subscriptionPlanRepository = subscriptionPlanRepository;
    }

    public override void Configure()
    {
        Get("/api/admin/user-subscriptions/user/{userId:guid}");
    }

    public override async Task HandleAsync(GetUserActiveSubscriptionRequest req, CancellationToken ct)
    {
        var subscription = await _userSubscriptionRepository.GetActiveSubscriptionAsync(req.UserId, ct);
        if (subscription == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }
        var plan = await _subscriptionPlanRepository.GetByIdAsync(subscription.SubscriptionPlanId, ct);
        var response = new UserSubscriptionResponse(
            subscription.Id,
            subscription.UserId,
            subscription.SubscriptionPlanId,
            plan?.Name ?? "Unknown Plan",
            subscription.StartDate,
            subscription.ExpiryDate,
            subscription.CurrentMonthUsage,
            subscription.IsActive,
            subscription.IsExpired(),
            subscription.PaymentReference
        );
        await SendAsync(response, cancellation: ct);
    }
}

public class GetUserActiveSubscriptionRequest
{
    public Guid UserId { get; set; }
} 