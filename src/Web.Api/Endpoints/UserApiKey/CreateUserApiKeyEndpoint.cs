using Application.Features.UserApiKey.CreateUserApiKey;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.UserApiKeys;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.UserApiKey;

[Authorize]
public class CreateUserApiKeyEndpoint : Endpoint<UserApiKeyRequest>
{
    public override void Configure()
    {
        Post("/api/userapikey/Create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UserApiKeyRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new CreateUserApiKeyCommand(
            Guid.Parse(userId),
            req.AiProviderId,
            req.ApiKey
        ).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 