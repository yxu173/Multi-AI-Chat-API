using Application.Features.Chats.GetSharedChat;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[AllowAnonymous]
public class GetSharedChatRequest
{
    public string Token { get; set; } = string.Empty;
}

public class GetSharedChatEndpoint : Endpoint<GetSharedChatRequest>
{
    public override void Configure()
    {
        Get("/api/chat/Shared/{Token}");
        AllowAnonymous();
        Description(x => x.Produces(200).Produces(404).Produces(500));
    }

    public override async Task HandleAsync(GetSharedChatRequest req, CancellationToken ct)
    {
        var result = await new GetSharedChatQuery(req.Token).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 404, ct);
    }
} 