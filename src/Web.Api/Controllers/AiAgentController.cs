using Application.Features.AiAgents.CreateAiAgent;
using Application.Features.AiAgents.GetAiAgentById;
using Application.Features.AiAgents.GetAllAiAgents;
using Application.Features.AiAgents.UpdateAiAgent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Contracts.AiAgents;
using Web.Api.Extensions;
using Web.Api.Infrastructure;
using PluginInfo = Application.Features.AiAgents.CreateAiAgent.PluginInfo;

namespace Web.Api.Controllers;

[Authorize]
public class AiAgentController : BaseController
{
    [HttpPost("Create")]
    public async Task<IResult> CreateAiAgent([FromBody] CreateAiAgentRequest request)
    {
        var command = new CreateAiAgentCommand(
            UserId,
            request.Name,
            request.Description,
            request.SystemInstructions,
            request.DefaultModel,
            request.IconUrl,
            request.Categories,
            request.AssignCustomModelParameters,
            request.Temperature,
            request.PresencePenalty,
            request.FrequencyPenalty,
            request.TopP,
            request.TopK,
            request.MaxTokens,
            request.EnableThinking,
            request.StopSequences,
            request.PromptCaching,
            request.ContextLimit,
            request.SafetySettings,
            request.ProfilePictureUrl,
            request.Plugins?.Select(p => new PluginInfo(p.PluginId, p.IsActive)).ToList()
        );

        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("GetAllAgentsByCategories")]
    public async Task<IResult> GetAllAgentsByCategories()
    {
        var query = new GetAllAiAgentsQuery(UserId);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpGet("{id}")]
    public async Task<IResult> GetById(Guid id)
    {
        var query = new GetAiAgentByIdQuery(UserId, id);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [HttpPut("{id}")]
    public async Task<IResult> UpdateAiAgent(Guid id, [FromBody] CreateAiAgentRequest request)
    {
        var command = new UpdateAiAgentCommand(
            UserId,
            id,
            request.Name,
            request.Description,
            request.SystemInstructions,
            request.DefaultModel,
            request.IconUrl,
            request.Categories,
            request.AssignCustomModelParameters,
            request.Temperature,
            request.PresencePenalty,
            request.FrequencyPenalty,
            request.TopP,
            request.TopK,
            request.MaxTokens,
            request.EnableThinking,
            request.StopSequences,
            request.PromptCaching,
            request.ContextLimit,
            request.SafetySettings,
            request.ProfilePictureUrl,
            request.Plugins?.Select(p => new PluginInfo(p.PluginId, p.IsActive)).ToList()
        );

        var result = await _mediator.Send(command);
        return result.Match(val => Results.Ok(val), CustomResults.Problem);
    }

    [HttpGet("GetAllCategories")]
    public IActionResult GetAllCategories()
    {
        var categories = Enum.GetNames(typeof(Domain.Enums.AgentCategories));
        return Ok(categories);
    }
}