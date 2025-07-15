using Application.Features.ChatFolders.DeleteChatFolder;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.ChatFolders;

[Authorize]
public class DeleteChatFolderRequest
{
    public Guid Id { get; set; }
}

public class DeleteChatFolderEndpoint : Endpoint<DeleteChatFolderRequest>
{
    public override void Configure()
    {
        Delete("/api/chatfolder/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(DeleteChatFolderRequest req, CancellationToken ct)
    {
        var result = await new DeleteChatFolderCommand(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 