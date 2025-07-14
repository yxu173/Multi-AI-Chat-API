using Application.Features.Chats.MoveChatToFolder;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.Chats;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class MoveChatToFolderRequestWithId 
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
}

public class MoveChatToFolderEndpoint : Endpoint<MoveChatToFolderRequestWithId>
{
    public override void Configure()
    {
        Put("/api/chat/Update/{Id}/MoveToFolder");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(MoveChatToFolderRequestWithId req, CancellationToken ct)
    {
        var result = await new MoveChatToFolderCommand(req.Id, req.FolderId).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 