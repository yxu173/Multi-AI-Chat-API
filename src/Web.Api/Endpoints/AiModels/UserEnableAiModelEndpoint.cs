using Application.Features.AiModels.Commands.UserEnableAiModel;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.AiModels;

[Authorize]
public class UserEnableAiModelRequest
{
    public Guid Id { get; set; }
}

public class UserEnableAiModelEndpoint : Endpoint<UserEnableAiModelRequest>
{
    public override void Configure()
    {
        Put("/api/aimodel/UserModels/{Id}/Enable");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UserEnableAiModelRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new UserEnableAiModelCommand(Guid.Parse(userId), req.Id).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 