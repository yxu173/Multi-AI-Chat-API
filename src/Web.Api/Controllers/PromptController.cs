using Application.Features.Prompts.CreatePrompt;
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
        var command = new CreatePromptCommand(UserId, request.Title, request.Description, request.Content);
        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}