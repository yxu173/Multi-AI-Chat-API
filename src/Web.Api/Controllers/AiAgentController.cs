using Application.Features.AiAgents.CreateAiAgent;
using Application.Features.AiAgents.GetAllAiAgents;
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
        var command = new CreateAiAgentCommand(
            UserId,
            request.Name,
            request.Description,
            request.SystemPrompt,
            request.AiModelId,
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
            request.ReasoningEffort,
            request.PromptCaching,
            request.ContextLimit,
            request.SafetySettings,
            request.ProfilePictureUrl,
            request.Plugins?.Select(p => new PluginInfo(p.PluginId, p.Order, p.IsActive)).ToList()
        );

        var result = await _mediator.Send(command);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
    
    [HttpGet("GetAll")]
    public async Task<IResult> GetAllAgents()
    {
        var query = new GetAllAiAgentsQuery(UserId);
        var result = await _mediator.Send(query);
        return result.Match(Results.Ok, CustomResults.Problem);
    }
}