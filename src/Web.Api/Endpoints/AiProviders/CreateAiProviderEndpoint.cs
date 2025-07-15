using Application.Features.AiProviders.CreateAiProvider;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.AiProviders;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiProviders;

[Authorize]
public class CreateAiProviderEndpoint : Endpoint<AiProviderRequest>
{
    public override void Configure()
    {
        Post("/api/aiprovider/Create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(AiProviderRequest req, CancellationToken ct)
    {
        var result = await new CreateAiProviderCommand(req.Name, req.Description).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 