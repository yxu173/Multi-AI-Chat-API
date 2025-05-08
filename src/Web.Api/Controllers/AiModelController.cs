using Application.Features.AiModels.Commands.CreateAiModel;
using Application.Features.AiModels.Commands.EnableAiModel;
using Application.Features.AiModels.Commands.UserEnableAiModel;
using Application.Features.AiModels.Queries.GetAiModelById;
using Application.Features.AiModels.Queries.GetAllAiModels;
using Application.Features.AiModels.Queries.GetEnabledAiModels;
using Application.Features.AiModels.Queries.GetUserAiModels;
using Application.Features.AiModels.Queries.GetUserAiModelsEnabled;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.AiModels;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class AiModelController : BaseController
{
    [Microsoft.AspNetCore.Mvc.HttpPost("Create")]
    public async Task<IResult> Create([Microsoft.AspNetCore.Mvc.FromBody] AiModelRequest request)
    {
        var result = await new CreateAiModelCommand(
            request.Name,
            request.ModelType,
            request.AiProviderId,
            request.InputTokenPricePer1M,
            request.OutputTokenPricePer1M,
            request.ModelCode,
            request.MaxInputTokens,
            request.MaxOutputTokens,
            request.IsEnabled,
            request.SupportsThinking,
            request.SupportsVision,
            request.ContextLength,
            request.ApiType,
            request.PluginsSupported,
            request.StreamingOutputSupported,
            request.SystemRoleSupported,
            request.PromptCachingSupported
        ).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAll")]
    public async Task<IResult> GetAll()
    {
        var result = await new GetAllAiModelsQuery().ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id:guid}")]
    public async Task<IResult> GetById([FromRoute] Guid id)
    {
        var result = await new GetAiModelByIdQuery(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPatch("{id:guid}")]
    public async Task<IResult> SetEnabledAiModel([FromRoute] Guid id)
    {
        var result = await new EnableAiModelCommand(id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("EnabledModels")]
    public async Task<IResult> GetEnabledAiModels()
    {
        var result = await new GetEnabledAiModelsQuery().ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }


    [Microsoft.AspNetCore.Mvc.HttpGet("GetUserEnabledAiModel/me")]
    public async Task<IResult> GetMyEnabledAiModels()
    {
        var result = await new GetEnabledAiModelsByUserIdQuery(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("me/UserAiModels")]
    public async Task<IResult> GetMyAiModels()
    {
        var result = await new GetUserAiModelsQuery(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("UserModels/{id:guid}/Enable")]
    public async Task<IResult> EnableAiModel([FromRoute] Guid id)
    {
        var result = await new UserEnableAiModelCommand(UserId, id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}