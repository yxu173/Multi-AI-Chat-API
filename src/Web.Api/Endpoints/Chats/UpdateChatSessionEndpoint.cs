using Application.Features.Chats.UpdateChatSession;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Chats;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class UpdateChatSessionRequestWithId
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class UpdateChatSessionEndpoint : Endpoint<UpdateChatSessionRequestWithId>
{
    public override void Configure()
    {
        Put("/api/chat/Update/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UpdateChatSessionRequestWithId req, CancellationToken ct)
    {
        var result = await new UpdateChatSessionCommand(req.Id, req.Title).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 