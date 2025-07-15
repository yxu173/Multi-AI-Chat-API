using Application.Features.ChatFolders.GetChatsFolderByUserId;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.ChatFolders;

[Authorize]
public class GetAllChatFoldersEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/chatfolder/GetAll");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new GetChatsFolderByUserIdQuery(Guid.Parse(userId)).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 