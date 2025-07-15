using Application.Features.Admin.ProviderApiKeys.GetProviderApiKeys;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.ProviderApiKeys;

[Authorize(Roles = "Admin")]
public class GetProviderApiKeyByIdRequest
{
    public Guid Id { get; set; }
}

public class GetProviderApiKeyByIdEndpoint : Endpoint<GetProviderApiKeyByIdRequest, ProviderApiKeyResponse>
{
    public override void Configure()
    {
        Get("/api/admin/provider-keys/{Id:guid}");
        Description(x => x.Produces(200, typeof(ProviderApiKeyResponse)).Produces(404).Produces(400));
    }

    public override async Task HandleAsync(GetProviderApiKeyByIdRequest req, CancellationToken ct)
    {
        var result = await new GetProviderApiKeysQuery().ExecuteAsync(ct: ct);
        if (result.IsSuccess)
        {
            var apiKey = result.Value.FirstOrDefault(k => k.Id == req.Id);
            if (apiKey == null)
            {
                await SendNotFoundAsync(ct);
                return;
            }

            var response = new ProviderApiKeyResponse(
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
            await SendOkAsync(response, ct);
        }
        else
        {
            await SendAsync(null, 400, ct);
        }
    }
}