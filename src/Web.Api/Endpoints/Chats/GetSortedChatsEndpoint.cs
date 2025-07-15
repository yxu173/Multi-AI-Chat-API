using Application.Features.Chats.GetSortedChats;
using Domain.Enums;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class GetSortedChatsRequest
{
    public string SortOrder { get; set; } = string.Empty;
}

public class GetSortedChatsEndpoint : Endpoint<GetSortedChatsRequest>
{
    public override void Configure()
    {
        Get("/api/chat/GetAll/{SortOrder}");
        ResponseCache(120);
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(GetSortedChatsRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        if (!Enum.TryParse<ChatSortOrder>(req.SortOrder, out var sortOrder))
        {
            await SendErrorsAsync(400, ct);
            return;
        }
        var result = await new GetSortedChatsQuery(Guid.Parse(userId), sortOrder).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 