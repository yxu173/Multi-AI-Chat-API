using Application.Features.ChatFolders.GetChatFolderById;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.ChatFolders;

[Authorize]
public class GetChatFolderByIdRequest
{
    public Guid Id { get; set; }
}

public class GetChatFolderByIdEndpoint : Endpoint<GetChatFolderByIdRequest>
{
    public override void Configure()
    {
        Get("/api/chatfolder/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(GetChatFolderByIdRequest req, CancellationToken ct)
    {
        var result = await new GetChatFolderByIdQuery(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 