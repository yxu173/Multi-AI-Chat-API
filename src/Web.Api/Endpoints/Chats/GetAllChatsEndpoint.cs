using Application.Features.Chats.GetAllChatsByUserId;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class GetAllChatsRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class GetAllChatsEndpoint : Endpoint<GetAllChatsRequest>
{
    public override void Configure()
    {
        Get("/api/chat/GetAll");
        Description(x => x.Produces(200).Produces(500));
    }

    public override async Task HandleAsync(GetAllChatsRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new GetAllChatsByUserIdQuery(Guid.Parse(userId), req.Page, req.PageSize).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 