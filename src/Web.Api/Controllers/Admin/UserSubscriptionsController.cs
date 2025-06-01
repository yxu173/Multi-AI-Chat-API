using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Application.Features.Admin.UserSubscriptions;
using Application.Features.Admin.UserSubscriptions.AssignSubscriptionToUser;
using Application.Features.Admin.UserSubscriptions.CancelUserSubscription;
using Application.Features.Admin.UserSubscriptions.GetUserSubscriptions;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Web.Api.Contracts.Admin;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers.Admin;

[Route("api/admin/user-subscriptions")]
public class UserSubscriptionsController : AdminControllerBase
{
    private readonly IServiceProvider _serviceProvider;

    public UserSubscriptionsController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    [Microsoft.AspNetCore.Mvc.HttpGet]
    [ProducesResponseType(typeof(List<UserSubscriptionResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetUserSubscriptions(
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? userId = null,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? planId = null,
        [Microsoft.AspNetCore.Mvc.FromQuery] bool activeOnly = false)
    {
        var result = await new GetUserSubscriptionsQuery(userId, planId, activeOnly).ExecuteAsync();
        
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("assign")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> AssignSubscription([Microsoft.AspNetCore.Mvc.FromBody] AssignSubscriptionRequest request)
    {
        var command = new AssignSubscriptionToUserCommand(
            request.UserId,
            request.SubscriptionPlanId,
            request.StartDate,
            request.DurationMonths,
            request.PaymentReference
        );
        
        var result = await command.ExecuteAsync();
        
        return result.Match(
            id => Results.Created($"/api/admin/user-subscriptions/{id}", id),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> CancelSubscription([Microsoft.AspNetCore.Mvc.FromBody] CancelSubscriptionRequest request)
    {
        var command = new CancelUserSubscriptionCommand(request.SubscriptionId);
        var result = await command.ExecuteAsync();
        
        return result.Match(
            success => Results.NoContent(),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("user/{userId:guid}")]
    [ProducesResponseType(typeof(UserSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetUserActiveSubscription([FromRoute] Guid userId)
    {
        // Get the user's active subscription directly from repository
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserSubscriptionRepository>();
        var subscription = await repository.GetActiveSubscriptionAsync(userId);
        
        if (subscription == null)
        {
            return Results.NotFound();
        }
        
        return Results.Ok(await ToUserSubscriptionResponseAsync(subscription));
    }

    private async Task<UserSubscriptionResponse> ToUserSubscriptionResponseAsync(UserSubscription subscription)
    {
        // We need to get the subscription plan name
        using var scope = _serviceProvider.CreateScope();
        var planRepository = scope.ServiceProvider.GetRequiredService<ISubscriptionPlanRepository>();
        var plan = await planRepository.GetByIdAsync(subscription.SubscriptionPlanId);
        
        return new UserSubscriptionResponse(
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
    }

    private async Task<List<UserSubscriptionResponse>> ToUserSubscriptionResponsesAsync(
        IReadOnlyList<UserSubscription> subscriptions)
    {
        var responses = new List<UserSubscriptionResponse>();
        
        foreach (var subscription in subscriptions)
        {
            responses.Add(await ToUserSubscriptionResponseAsync(subscription));
        }
        
        return responses;
    }
}
