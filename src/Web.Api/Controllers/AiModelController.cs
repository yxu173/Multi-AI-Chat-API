using Application.Features.AiModels.Commands.CreateAiModel;
using Application.Features.AiModels.Commands.EnableAiModel;
using Application.Features.AiModels.Commands.UserEnableAiModel;
using Application.Features.AiModels.Queries.GetAiModelById;
using Application.Features.AiModels.Queries.GetAllAiModels;
using Application.Features.AiModels.Queries.GetEnabledAiModels;
using Application.Features.AiModels.Queries.GetUserAiModels;
using Application.Features.AiModels.Queries.GetUserAiModelsEnabled;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.AiModels;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class AiModelController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> Create([FromBody] AiModelRequest request)
    {
        var command = new CreateAiModelCommand(
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
        );
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("GetAll")]
    public async Task<IResult> GetAll()
    {
        var query = new GetAllAiModelsQuery();
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("{id:guid}")]
    public async Task<IResult> GetById([FromRoute] Guid id)
    {
        var query = new GetAiModelByIdQuery(id);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IResult> SetEnabledAiModel([FromRoute] Guid id)
    {
        var command = new EnableAiModelCommand(id);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("EnabledModels")]
    public async Task<IResult> GetEnabledAiModels()
    {
        var query = new GetEnabledAiModelsQuery();
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }


    [HttpGet("GetUserEnabledAiModel/me")]
    public async Task<IResult> GetMyEnabledAiModels()
    {
        var query = new GetEnabledAiModelsByUserIdQuery(UserId);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("me/UserAiModels")]
    public async Task<IResult> GetMyAiModels()
    {
        var query = new GetUserAiModelsQuery(UserId);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPut("UserModels/{id:guid}/Enable")]
    public async Task<IResult> EnableAiModel([FromRoute] Guid id)
    {
        var command = new UserEnableAiModelCommand(UserId, id);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}