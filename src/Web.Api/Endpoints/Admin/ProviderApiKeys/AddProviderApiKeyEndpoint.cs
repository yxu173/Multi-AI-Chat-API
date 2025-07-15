using Application.Features.Admin.ProviderApiKeys.AddProviderApiKey;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.Admin;

namespace Web.Api.Endpoints.Admin.ProviderApiKeys;

[Authorize(Roles = "Admin")]
public class AddProviderApiKeyEndpoint : Endpoint<AddProviderApiKeyRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/admin/provider-keys");
        Description(x => x.Produces(201, typeof(Guid)).Produces(400));
    }

    public override async Task HandleAsync(AddProviderApiKeyRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }

        var command = new AddProviderApiKeyCommand(
            req.AiProviderId,
            req.ApiSecret,
            req.Label,
            Guid.Parse(userId),
            req.MaxRequestsPerDay
        );
        var result = await command.ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendCreatedAtAsync(
                "GetProviderApiKeyById",
                new { id = result.Value },
                result.Value
            );
        else
            await SendAsync(Guid.Empty, 400, ct);
    }
}