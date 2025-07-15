using Application.Features.AiProviders.GetAiProviderById;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiProviders;

[Authorize]
public class GetAiProviderByIdRequest
{
    public Guid Id { get; set; }
}

public class GetAiProviderByIdEndpoint : Endpoint<GetAiProviderByIdRequest>
{
    public override void Configure()
    {
        Get("/api/aiprovider/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(GetAiProviderByIdRequest req, CancellationToken ct)
    {
        var result = await new GetAiProviderByIdQuery(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 