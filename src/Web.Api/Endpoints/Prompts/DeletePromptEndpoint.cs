using Application.Features.Prompts.DeletePrompt;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Prompts;

public class DeletePromptRequest
{
    public Guid Id { get; set; }
}
[Authorize]
public class DeletePromptEndpoint : Endpoint<DeletePromptRequest>
{
    public override void Configure()
    {
        Delete("/api/prompt/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(DeletePromptRequest req, CancellationToken ct)
    {
        var result = await new DeletePromptCommand(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 