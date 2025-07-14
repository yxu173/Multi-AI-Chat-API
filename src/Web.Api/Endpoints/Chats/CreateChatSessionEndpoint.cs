using Application.Features.Chats.CreateChatSession;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.Chats;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class CreateChatSessionEndpoint : Endpoint<CreateChatSessionRequest>
{
    public override void Configure()
    {
        Post("/api/chat/Create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CreateChatSessionRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }

        var result = await new CreateChatSessionCommand(
            req.ChatType,
            Guid.Parse(userId),
            req.ModelId,
            req.FolderId,
            req.AiAgentId,
            req.CustomApiKey,
            req.EnableThinking).ExecuteAsync(ct: ct);

        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 