using Application.Features.Admin.ProviderApiKeys;
using Application.Features.Admin.ProviderApiKeys.AddProviderApiKey;
using Application.Features.Admin.ProviderApiKeys.GetProviderApiKeys;
using Application.Features.Admin.ProviderApiKeys.UpdateProviderApiKey;
using Domain.Aggregates.Admin;
using FastEndpoints;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Admin;
using Web.Api.Extensions;

namespace Web.Api.Controllers.Admin;

[Route("api/admin/provider-keys")]
public class ProviderApiKeysController : AdminControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpGet]
    [ProducesResponseType(typeof(List<ProviderApiKeyResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> GetProviderApiKeys([Microsoft.AspNetCore.Mvc.FromQuery] Guid? providerId = null)
    {
        var result = await new GetProviderApiKeysQuery(providerId).ExecuteAsync();
        
        return result.Match(
            apiKeys => Results.Ok(apiKeys.ToApiKeyResponses()),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProviderApiKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetProviderApiKeyById([FromRoute] Guid id)
    {
        var result = await new GetProviderApiKeysQuery().ExecuteAsync();
        
        return result.Match(
            apiKeys => {
                var apiKey = apiKeys.FirstOrDefault(k => k.Id == id);
                if (apiKey == null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(apiKey.ToApiKeyResponse());
            },
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> AddProviderApiKey([Microsoft.AspNetCore.Mvc.FromBody] AddProviderApiKeyRequest request)
    {
        var command = new AddProviderApiKeyCommand(
            request.AiProviderId,
            request.ApiSecret,
            request.Label,
            UserId,
            request.MaxRequestsPerDay
        );
        
        var result = await command.ExecuteAsync();
        
        return result.Match(
            id => Results.Created($"/api/admin/provider-keys/{id}", id),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateProviderApiKey([FromRoute] Guid id, [Microsoft.AspNetCore.Mvc.FromBody] UpdateProviderApiKeyRequest request)
    {
        var command = new UpdateProviderApiKeyCommand(
            id,
            request.ApiSecret,
            request.Label,
            request.MaxRequestsPerDay,
            request.IsActive
        );
        
        var result = await command.ExecuteAsync();
        
        return result.Match(
            success => Results.NoContent(),
            error => Results.Problem(statusCode: StatusCodes.Status400BadRequest)
        );
    }
}

public static class ProviderApiKeyExtensions
{
    public static ProviderApiKeyResponse ToApiKeyResponse(this ProviderApiKey apiKey)
    {
        return new ProviderApiKeyResponse(
            apiKey.Id,
            apiKey.AiProviderId,
            apiKey.Label,
            "••••••••" + apiKey.Secret.Substring(Math.Max(0, apiKey.Secret.Length - 4)),
            apiKey.IsActive,
            apiKey.MaxRequestsPerDay,
            apiKey.UsageCountToday,
            apiKey.CreatedAt,
            apiKey.LastUsedTimestamp
        );
    }

    public static List<ProviderApiKeyResponse> ToApiKeyResponses(this IEnumerable<ProviderApiKey> apiKeys)
    {
        return apiKeys.Select(k => k.ToApiKeyResponse()).ToList();
    }
}
