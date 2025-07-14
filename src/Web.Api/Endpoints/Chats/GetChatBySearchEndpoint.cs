using Application.Features.Chats.GetChatBySeacrh;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class GetChatBySearchRequest
{
    public string Search { get; set; } = string.Empty;
}

public class GetChatBySearchEndpoint : Endpoint<GetChatBySearchRequest>
{
    public override void Configure()
    {
        Get("/api/chat/Search");
        Description(x => x.Produces(200).Produces(404).Produces(500));
    }

    public override async Task HandleAsync(GetChatBySearchRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new GetChatBySearchQuery(Guid.Parse(userId), req.Search).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 404, ct);
    }
} 