using Application.Features.UserAiModelSettings.UpdateUserAiModelSettings;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.UserSettings;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.UserSettings;

[Authorize]
public class UpdateUserAiModelSettingsEndpoint : Endpoint<UpdateUserAiModelSettingsRequest>
{
    public override void Configure()
    {
        Put("/api/usersettings/UpdateUserAiModelSettings");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UpdateUserAiModelSettingsRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new UpdateUserAiModelSettingsCommand(
            Guid.Parse(userId),
            req.AiModelId,
            req.SystemMessage,
            req.ContextLimit,
            req.Temperature,
            req.TopP,
            req.TopK,
            req.FrequencyPenalty,
            req.PresencePenalty,
            req.MaxTokens,
            req.SafetySettings,
            req.PromptCaching
        ).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 