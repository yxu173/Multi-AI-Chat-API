using Application.Features.AiModels.Commands.EnableAiModel;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiModels;

[Authorize]
public class EnableAiModelRequest
{
    public Guid Id { get; set; }
}

public class EnableAiModelEndpoint : Endpoint<EnableAiModelRequest>
{
    public override void Configure()
    {
        Patch("/api/aimodel/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(EnableAiModelRequest req, CancellationToken ct)
    {
        var result = await new EnableAiModelCommand(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 