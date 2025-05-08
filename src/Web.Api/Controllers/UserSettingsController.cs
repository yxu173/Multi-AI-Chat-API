using Application.Features.UserAiModelSettings.GetUserAiModelSettings;
using Application.Features.UserAiModelSettings.ResetSystemInstructions;
using Application.Features.UserAiModelSettings.UpdateUserAiModelSettings;
using FastEndpoints;
using Web.Api.Contracts.UserSettings;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

public class UserSettingsController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPut("UpdateUserAiModelSettings")]
    public async Task<IResult> UpdateUserAiModelSettings([Microsoft.AspNetCore.Mvc.FromBody] UpdateUserAiModelSettingsRequest request)
    {
        var result = await new UpdateUserAiModelSettingsCommand
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
        ).ExecuteAsync();

        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetUserAiModelSettings")]
    public async Task<IResult> GetUserAiModelSettings()
    {
        var result = await new GetUserAiModelSettingsCommand(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPatch("ResetSystemInstructions")]
    public async Task<IResult> ResetSystemInstructions()
    {
        var result = await new ResetSystemInstructionsCommand(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}