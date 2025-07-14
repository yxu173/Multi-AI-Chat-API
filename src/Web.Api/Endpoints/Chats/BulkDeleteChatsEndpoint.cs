using Application.Features.Chats.BulkDeleteChats;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.Chats;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class BulkDeleteChatsEndpoint : Endpoint<BulkDeleteChatsRequest>
{
    public override void Configure()
    {
        Delete("/api/chat/BulkDelete");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(BulkDeleteChatsRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new BulkDeleteChatsCommand(Guid.Parse(userId), req.ChatIds).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 