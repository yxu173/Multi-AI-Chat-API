using Application.Features.AiProviders.DeleteAiProvider;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiProviders;

[Authorize]
public class DeleteAiProviderRequest
{
    public Guid Id { get; set; }
}

public class DeleteAiProviderEndpoint : Endpoint<DeleteAiProviderRequest>
{
    public override void Configure()
    {
        Delete("/api/aiprovider/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(DeleteAiProviderRequest req, CancellationToken ct)
    {
        var result = await new DeleteAiProviderCommand(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 