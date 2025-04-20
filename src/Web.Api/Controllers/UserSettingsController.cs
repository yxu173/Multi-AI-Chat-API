using Application.Features.UserAiModelSettings.GetUserAiModelSettings;
using Application.Features.UserAiModelSettings.UpdateUserAiModelSettings;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.UserSettings;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class UserSettingsController : BaseController
{
    [HttpPut("UpdateUserAiModelSettings")]
    public async Task<IResult> UpdateUserAiModelSettings([FromBody] UpdateUserAiModelSettingsRequest request)
    {
        var command = new UpdateUserAiModelSettingsCommand
        (
            UserId,
            request.AiModelId,
            request.SystemMessage,
            request.ContextLimit,
            request.Temperature,
            request.TopP,
            request.TopK,
            request.FrequencyPenalty,
            request.PresencePenalty,
            request.MaxTokens,
            request.SafetySettings,
            request.PromptCaching
        );

        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("GetUserAiModelSettings")]
    public async Task<IResult> GetUserAiModelSettings()
    {
        var command = new GetUserAiModelSettingsCommand(UserId);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}