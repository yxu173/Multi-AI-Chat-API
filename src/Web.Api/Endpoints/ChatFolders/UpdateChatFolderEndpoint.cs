using Application.Features.ChatFolders.UpdateChatFolder;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Contracts.ChatFolders;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.ChatFolders;

[Authorize]
public class UpdateChatFolderRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UpdateChatFolderEndpoint : Endpoint<UpdateChatFolderRequest>
{
    public override void Configure()
    {
        Put("/api/chatfolder/update/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UpdateChatFolderRequest req, CancellationToken ct)
    {
        var result = await new UpdateChatFolderCommand(req.Id, req.Name).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 