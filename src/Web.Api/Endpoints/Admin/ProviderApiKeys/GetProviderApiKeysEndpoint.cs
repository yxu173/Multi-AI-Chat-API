using Application.Features.Admin.ProviderApiKeys.GetProviderApiKeys;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.ProviderApiKeys;

[Authorize(Roles = "Admin")]
public class GetProviderApiKeysRequest
{
    public Guid? ProviderId { get; set; }
}

public class GetProviderApiKeysEndpoint : Endpoint<GetProviderApiKeysRequest, List<ProviderApiKeyResponse>>
{
    public override void Configure()
    {
        Get("/api/admin/provider-keys");
        Description(x => x.Produces(200, typeof(List<ProviderApiKeyResponse>)).Produces(400));
    }

    public override async Task HandleAsync(GetProviderApiKeysRequest req, CancellationToken ct)
    {
        var result = await new GetProviderApiKeysQuery(req.ProviderId).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
        {
            var responses = result.Value.Select(apiKey => new ProviderApiKeyResponse(
                apiKey.Id,
                apiKey.AiProviderId,
                apiKey.Label,
                "••••••••" + apiKey.Secret.Substring(Math.Max(0, apiKey.Secret.Length - 4)),
                apiKey.IsActive,
                apiKey.MaxRequestsPerDay,
                apiKey.UsageCountToday,
                apiKey.CreatedAt,
                apiKey.LastUsedTimestamp
            )).ToList();
            await SendOkAsync(responses, ct);
        }
        else
        {
            await SendAsync(new List<ProviderApiKeyResponse>(), 400, ct);
        }
    }
}