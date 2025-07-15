using Application.Features.AiAgents.GetAiAgentById;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiAgents;

[Authorize]
public class GetAiAgentByIdRequest
{
    public Guid Id { get; set; }
}

public class GetAiAgentByIdEndpoint : Endpoint<GetAiAgentByIdRequest>
{
    public override void Configure()
    {
        Get("/api/aiagent/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(GetAiAgentByIdRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new GetAiAgentByIdQuery(Guid.Parse(userId), req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 