using Application.Features.AiModels.CreateAiModel;
using Application.Features.AiModels.EnableAiModel;
using Application.Features.AiModels.GetAllAiModels;
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
}