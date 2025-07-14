using Application.Features.Prompts.UpdatePrompt;
using FastEndpoints;
using System.Security.Claims;
using Web.Api.Contracts.Prompts;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Prompts;

public class UpdatePromptRequest
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new List<string>();
}

public class UpdatePromptEndpoint : Endpoint<UpdatePromptRequest>
{
    public override void Configure()
    {
        Put("/api/prompt/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UpdatePromptRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }

        var result =
            await new UpdatePromptCommand(req.Id, Guid.Parse(userId), req.Title, req.Description, req.Content, req.Tags)
                .ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
}