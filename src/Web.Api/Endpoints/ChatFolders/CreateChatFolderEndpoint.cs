using Application.Features.ChatFolders.CreateChatFolder;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.ChatFolders;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.ChatFolders;

[Authorize]
public class CreateChatFolderEndpoint : Endpoint<CreateFolderRequest>
{
    public override void Configure()
    {
        Post("/api/chatfolder/Create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CreateFolderRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new CreateChatFolderCommand(Guid.Parse(userId), req.Name).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 