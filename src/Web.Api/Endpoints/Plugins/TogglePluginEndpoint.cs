using Application.Features.Plugins.ToggleUserPlugin;
using FastEndpoints;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Plugins;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Plugins;
[Authorize]
public class TogglePluginEndpoint : Endpoint<TogglePluginRequest>
{
    public override void Configure()
    {
        Post("/api/plugin/toggle");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(TogglePluginRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new ToggleUserPluginCommand(Guid.Parse(userId), req.PluginId, req.IsEnabled).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 