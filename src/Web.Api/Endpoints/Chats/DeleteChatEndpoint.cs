using Application.Features.Chats.DeleteChatById;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class DeleteChatRequest
{
    public Guid Id { get; set; }
}

public class DeleteChatEndpoint : Endpoint<DeleteChatRequest>
{
    public override void Configure()
    {
        Delete("/api/chat/{Id}");
        Description(x => x.Produces(200).Produces(404).Produces(500));
    }

    public override async Task HandleAsync(DeleteChatRequest req, CancellationToken ct)
    {
        var result = await new DeleteChatCommand(req.Id).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 404, ct);
    }
} 