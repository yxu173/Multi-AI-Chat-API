using Application.Features.AiProviders.GetAllAiProviders;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiProviders;

[Authorize]
public class GetAllAiProvidersEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/aiprovider/GetAll");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await new GetAllAiProvidersQuery().ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 