using Application.Features.UserApiKey.UpdateUserApiKey;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.UserApiKey;

[Authorize]
public class UpdateUserApiKeyRequest
{
    public Guid Id { get; set; }
    public string UserApiKey { get; set; } = string.Empty;
}

public class UpdateUserApiKeyEndpoint : Endpoint<UpdateUserApiKeyRequest>
{
    public override void Configure()
    {
        Put("/api/userapikey/update/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UpdateUserApiKeyRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new UpdateUserApiKeyCommand(Guid.Parse(userId), req.Id, req.UserApiKey).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(Web.Api.Infrastructure.CustomResults.Problem(result), 400, ct);
    }
} 