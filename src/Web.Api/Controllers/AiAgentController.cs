using Application.Features.AiAgents.CreateAiAgent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.AiAgents;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class AiAgentController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> CreateAiAgent([FromBody] CreateAiAgentRequest request)
    {
        var command = new CreateAiAgentCommand
        (
            UserId,
            request.Name,
            request.Description,
            request.SystemPrompt,
            request.AiModelId,
            request.IconUrl,
            request.Categories,
            request.AssignCustomModelParameters,
            request.ModelParameters,
            request.ProfilePictureUrl,
            request.Plugins?.Select(p => new PluginInfo(p.PluginId, p.Order, p.IsActive)).ToList()
        );

        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}