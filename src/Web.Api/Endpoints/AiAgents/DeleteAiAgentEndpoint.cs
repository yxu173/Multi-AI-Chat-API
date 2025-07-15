using Application.Features.AiAgents.DeleteAiAgent;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiAgents;

[Authorize]
public class DeleteAiAgentRequest
{
    public Guid Id { get; set; }
}

public class DeleteAiAgentEndpoint : Endpoint<DeleteAiAgentRequest>
{
    public override void Configure()
    {
        Delete("/api/aiagent/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(DeleteAiAgentRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new DeleteAiAgentCommand(Guid.Parse(userId), req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 