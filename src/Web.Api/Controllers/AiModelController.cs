using Application.Features.AiModels.CreateAiModel;
using Application.Features.AiModels.EnableAiModel;
using Application.Features.AiModels.GetAllAiModels;
using Application.Features.AiModels.GetEnabledAiModels;
using Application.Features.AiModels.GetUserAiModels;
using Application.Features.AiModels.GetUserAiModelsEnabled;
using Application.Features.AiModels.UserEnableAiModel;
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
            request.AiProvider,
            request.InputTokenPricePer1K,
            request.OutputTokenPricePer1K,
            request.ModelCode
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