using Application.Features.AiModels.Queries.GetAiModelById;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiModels;

[Authorize]
public class GetAiModelByIdRequest
{
    public Guid Id { get; set; }
}

public class GetAiModelByIdEndpoint : Endpoint<GetAiModelByIdRequest>
{
    public override void Configure()
    {
        Get("/api/aimodel/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(GetAiModelByIdRequest req, CancellationToken ct)
    {
        var result = await new GetAiModelByIdQuery(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 