using Application.Features.Prompts.GetAllPromptsByUserId;
using FastEndpoints;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Prompts;

public class GetAllPromptsRequest
{
    public Guid Id { get; set; }
}

public class GetAllPromptsEndpoint : Endpoint<GetAllPromptsRequest>
{
    public override void Configure()
    {
        Get("/api/prompt/{Id}/GetAllPrompts");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(GetAllPromptsRequest req, CancellationToken ct)
    {
        var result = await new GetAllPromptsByUserIdQuery(req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
}