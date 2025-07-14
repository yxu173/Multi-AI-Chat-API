using Application.Features.Plugins.CreatePlugin;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Plugins;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Plugins;
[Authorize]
public class CreatePluginEndpoint : Endpoint<CreatePluginRequest>
{
    public override void Configure()
    {
        Post("/api/plugin/create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CreatePluginRequest req, CancellationToken ct)
    {
        var result = await new CreatePluginCommand(req.Name, req.Description, req.IconUrl).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 