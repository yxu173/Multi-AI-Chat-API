using Application.Features.Chats.GetChatDetails;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class GetChatDetailsRequest
{
    public Guid Id { get; set; }
}

public class GetChatDetailsEndpoint : Endpoint<GetChatDetailsRequest>
{
    public override void Configure()
    {
        Get("/api/chat/{Id}/Details");
        Description(x => x.Produces(200).Produces(404).Produces(500));
    }

    public override async Task HandleAsync(GetChatDetailsRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new GetChatDetailsQuery(Guid.Parse(userId), req.Id).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 404, ct);
    }
} 