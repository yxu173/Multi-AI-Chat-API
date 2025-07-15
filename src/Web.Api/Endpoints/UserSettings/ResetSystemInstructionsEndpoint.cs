using Application.Features.UserAiModelSettings.ResetSystemInstructions;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.UserSettings;

[Authorize]
public class ResetSystemInstructionsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Patch("/api/usersettings/ResetSystemInstructions");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new ResetSystemInstructionsCommand(Guid.Parse(userId)).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 