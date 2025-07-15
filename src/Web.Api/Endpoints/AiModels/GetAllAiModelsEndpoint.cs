using Application.Features.AiModels.Queries.GetAllAiModels;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiModels;

[Authorize]
public class GetAllAiModelsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/aimodel/GetAll");
        ResponseCache(120);
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await new GetAllAiModelsQuery().ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 