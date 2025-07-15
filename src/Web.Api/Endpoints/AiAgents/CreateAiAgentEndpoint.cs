using Application.Features.AiAgents.CreateAiAgent;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.AiAgents;
using Web.Api.Infrastructure;
using PluginInfo = Application.Features.AiAgents.CreateAiAgent.PluginInfo;

namespace Web.Api.Endpoints.AiAgents;

[Authorize]
public class CreateAiAgentEndpoint : Endpoint<CreateAiAgentRequest>
{
    public override void Configure()
    {
        Post("/api/aiagent/Create");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(CreateAiAgentRequest req, CancellationToken ct)
    {
        if (req.SystemInstructions == null) { throw new ArgumentNullException(nameof(req.SystemInstructions)); }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new CreateAiAgentCommand(
            Guid.Parse(userId),
            req.Name,
            req.Description,
            req.SystemInstructions,
            req.DefaultModel,
            req.Categories,
            req.AssignCustomModelParameters,
            req.Temperature,
            req.PresencePenalty,
            req.FrequencyPenalty,
            req.TopP,
            req.TopK,
            req.MaxTokens,
            req.EnableThinking,
            req.PromptCaching,
            req.ContextLimit,
            req.SafetySettings,
            req.ProfilePictureUrl,
            req.Plugins?.Select(p => new PluginInfo(p.PluginId, p.IsActive)).ToList()
        ).ExecuteAsync(ct: ct);
        if (result.IsSuccess)
            await SendOkAsync(result.Value, ct);
        else
            await SendAsync(CustomResults.Problem(result), 400, ct);
    }
} 