using Application.Features.Chats.ShareChat;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.Chats;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class ShareChatRequestWithId 
{
    public Guid Id { get; set; }
    public DateTime? ExpiresAt { get; set; } = null;
}

public class ShareChatEndpoint : Endpoint<ShareChatRequestWithId>
{
    public override void Configure()
    {
        Post("/api/chat/{Id}/Share");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(ShareChatRequestWithId req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new ShareChatCommand(req.Id, Guid.Parse(userId), req.ExpiresAt).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 