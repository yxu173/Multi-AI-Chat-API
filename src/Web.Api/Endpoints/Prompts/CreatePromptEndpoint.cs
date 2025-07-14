using Application.Features.Prompts.CreatePrompt;
using FastEndpoints;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Prompts;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Prompts;

[Authorize]
public class CreatePromptEndpoint : Endpoint<CreatePromptRequest>
{
    public override void Configure()
    {
        Post("/api/prompt/Create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CreatePromptRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }

        var result =
            await new CreatePromptCommand(Guid.Parse(userId), req.Title, req.Description, req.Content, req.Tags)
                .ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
}