using Application.Features.AiAgents.CreateAiAgent;
using Application.Features.AiAgents.DeleteAiAgent;
using Application.Features.AiAgents.GetAiAgentById;
using Application.Features.AiAgents.GetAllAiAgents;
using Application.Features.AiAgents.UpdateAiAgent;
using FastEndpoints;
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
    [Microsoft.AspNetCore.Mvc.HttpPost("Create")]
    public async Task<IResult> CreateAiAgent([Microsoft.AspNetCore.Mvc.FromBody] CreateAiAgentRequest request)
    {
        if (request.SystemInstructions == null) { throw new ArgumentNullException(nameof(request.SystemInstructions)); }
        var result = await new CreateAiAgentCommand(
            UserId,
            request.Name,
            request.Description,
            request.SystemInstructions,
            request.DefaultModel,
            request.Categories,
            request.AssignCustomModelParameters,
            request.Temperature,
            request.PresencePenalty,
            request.FrequencyPenalty,
            request.TopP,
            request.TopK,
            request.MaxTokens,
            request.EnableThinking,
            request.PromptCaching,
            request.ContextLimit,
            request.SafetySettings,
            request.ProfilePictureUrl,
            request.Plugins?.Select(p => new PluginInfo(p.PluginId, p.IsActive)).ToList()
        ).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAllAgentsByCategories")]
    public async Task<IResult> GetAllAgentsByCategories()
    {
        var result = await new GetAllAiAgentsQuery(UserId).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id}")]
    public async Task<IResult> GetById(Guid id)
    {
        var result = await new GetAiAgentByIdQuery(UserId, id).ExecuteAsync();
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("{id}")]
    public async Task<IResult> UpdateAiAgent(Guid id, [Microsoft.AspNetCore.Mvc.FromBody] CreateAiAgentRequest request)
    {
        if (request.SystemInstructions == null) { throw new ArgumentNullException(nameof(request.SystemInstructions)); }
        var result = await new UpdateAiAgentCommand(
            UserId,
            id,
            request.Name,
            request.Description,
            request.SystemInstructions,
            request.DefaultModel,
            request.Categories,
            request.AssignCustomModelParameters,
            request.Temperature,
            request.PresencePenalty,
            request.FrequencyPenalty,
            request.TopP,
            request.TopK,
            request.MaxTokens,
            request.EnableThinking,
            request.PromptCaching,
            request.ContextLimit,
            request.SafetySettings,
            request.ProfilePictureUrl,
            request.Plugins?.Select(p => new PluginInfo(p.PluginId, p.IsActive)).ToList()
        ).ExecuteAsync();

        return result.Match(val => Results.Ok(val), CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("{id:guid}")]
    public async Task<IResult> DeleteAiAgent([FromRoute] Guid id)
    {
        var result = await new DeleteAiAgentCommand(UserId, id).ExecuteAsync(); 
        return result.Match(Results.Ok, CustomResults.Problem);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("GetAllCategories")]
    public IActionResult GetAllCategories()
    {
        var categories = Enum.GetNames(typeof(Domain.Enums.AgentCategories));
        return Ok(categories);
    }
}