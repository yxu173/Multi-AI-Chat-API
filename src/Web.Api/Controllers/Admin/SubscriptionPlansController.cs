using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Application.Features.Admin.SubscriptionPlans;
using Application.Features.Admin.SubscriptionPlans.CreateSubscriptionPlan;
using Application.Features.Admin.SubscriptionPlans.GetSubscriptionPlans;
using Application.Features.Admin.SubscriptionPlans.UpdateSubscriptionPlan;
using Domain.Aggregates.Admin;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Admin;
using Web.Api.Extensions;

namespace Web.Api.Controllers.Admin;

[Route("api/admin/subscription-plans")]
public class SubscriptionPlansController : AdminControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpGet]
    [ProducesResponseType(typeof(List<SubscriptionPlanResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetSubscriptionPlans([Microsoft.AspNetCore.Mvc.FromQuery] bool activeOnly = false)
    {
        var result = await new GetSubscriptionPlansQuery(activeOnly).ExecuteAsync();
        
        return result.Match(
            plans => Results.Ok(plans.ToSubscriptionPlanResponses()),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SubscriptionPlanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetSubscriptionPlanById([FromRoute] Guid id)
    {
        var result = await new GetSubscriptionPlansQuery().ExecuteAsync();
        
        return result.Match(
            plans => {
                var plan = plans.FirstOrDefault(p => p.Id == id);
                if (plan == null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(plan.ToSubscriptionPlanResponse());
            },
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateSubscriptionPlan([Microsoft.AspNetCore.Mvc.FromBody] CreateSubscriptionPlanRequest request)
    {
        var command = new CreateSubscriptionPlanCommand(
            request.Name,
            request.Description,
            request.MaxRequestsPerDay,
            request.MaxTokensPerRequest,
            request.MonthlyPrice
        );
        
        var result = await command.ExecuteAsync();
        
        return result.Match(
            id => Results.Created($"/api/admin/subscription-plans/{id}", id),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateSubscriptionPlan([FromRoute] Guid id, [Microsoft.AspNetCore.Mvc.FromBody] UpdateSubscriptionPlanRequest request)
    {
        var command = new UpdateSubscriptionPlanCommand(
            id,
            request.Name,
            request.Description,
            request.MaxRequestsPerDay,
            request.MaxTokensPerRequest,
            request.MonthlyPrice,
            request.IsActive
        );
        
        var result = await command.ExecuteAsync();
        
        return result.Match(
            success => Results.NoContent(),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }
}

public static class SubscriptionPlanExtensions
{
    public static SubscriptionPlanResponse ToSubscriptionPlanResponse(this SubscriptionPlan plan)
    {
        return new SubscriptionPlanResponse(
            plan.Id,
            plan.Name,
            plan.Description,
            plan.MaxRequestsPerDay,
            plan.MaxTokensPerRequest,
            plan.MonthlyPrice,
            plan.IsActive,
            plan.CreatedAt,
            plan.LastModified
        );
    }

    public static List<SubscriptionPlanResponse> ToSubscriptionPlanResponses(this IEnumerable<SubscriptionPlan> plans)
    {
        return plans.Select(p => p.ToSubscriptionPlanResponse()).ToList();
    }
}
