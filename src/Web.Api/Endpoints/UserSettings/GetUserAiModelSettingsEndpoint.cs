using Application.Features.UserAiModelSettings.GetUserAiModelSettings;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.UserSettings;

[Authorize]
public class GetUserAiModelSettingsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/usersettings/GetUserAiModelSettings");
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
        var result = await new GetUserAiModelSettingsCommand(Guid.Parse(userId)).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 