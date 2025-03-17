using Application.Features.Prompts.CreatePrompt;
using Application.Features.Prompts.DeletePrompt;
using Application.Features.Prompts.GetAllPromptsByUserId;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.Prompts;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class PromptController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> CreatePrompt([FromBody] CreatePromptRequest request)
    {
        var command =
            new CreatePromptCommand(UserId, request.Title, request.Description, request.Content, request.Tags);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("GetMyPrompts")]
    public async Task<IResult> GetMyPrompts()
    {
        var query = new GetAllPromptsByUserIdQuery(UserId);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("{id}/GetAllPrompts")]
    public async Task<IResult> GetAllPrompts([FromRoute] Guid id)
    {
        var query = new GetAllPromptsByUserIdQuery(id);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpDelete("{id}")]
    public async Task<IResult> DeletePrompt([FromRoute] Guid id)
    {
        var command = new DeletePromptCommand(id);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}