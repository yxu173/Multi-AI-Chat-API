using Application.Features.Chats.GetChatById;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chats;

[Authorize]
public class GetChatByIdRequest
{
    public Guid Id { get; set; }
}

public class GetChatByIdEndpoint : Endpoint<GetChatByIdRequest>
{
    public override void Configure()
    {
        Get("/api/chat/{Id}");
        Description(x => x.Produces(200).Produces(404).Produces(500));
    }

    public override async Task HandleAsync(GetChatByIdRequest req, CancellationToken ct)
    {
        var result = await new GetChatByIdQuery(req.Id).ExecuteAsync();
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 404, ct);
    }
} 