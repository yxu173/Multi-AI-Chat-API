using Application.Features.AiAgents.UpdateAiAgent;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Web.Api.Contracts.AiAgents;
using Web.Api.Infrastructure;
using PluginInfo = Application.Features.AiAgents.CreateAiAgent.PluginInfo;

namespace Web.Api.Endpoints.AiAgents;

[Authorize]
public class UpdateAiAgentRequest
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? SystemInstructions { get; set; }
    public Guid DefaultModel { get; set; }
    public List<string>? Categories { get; set; }
    public bool AssignCustomModelParameters { get; set; }
    public double? Temperature { get; set; }
    public double? PresencePenalty { get; set; }
    public double? FrequencyPenalty { get; set; }
    public double? TopP { get; set; }
    public int? TopK { get; set; }
    public int? MaxTokens { get; set; }
    public bool? EnableThinking { get; set; }
    public bool? PromptCaching { get; set; }
    public int? ContextLimit { get; set; }
    public string? SafetySettings { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public List<PluginRequest>? Plugins { get; set; }
}


public class UpdateAiAgentEndpoint : Endpoint<UpdateAiAgentRequest>
{
    public override void Configure()
    {
        Put("/api/aiagent/{Id}");
        Description(x => x.Produces(200).Produces(400).Produces(500));
    }

    public override async Task HandleAsync(UpdateAiAgentRequest req, CancellationToken ct)
    {
        if (req.SystemInstructions == null) { throw new ArgumentNullException(nameof(req.SystemInstructions)); }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            await SendErrorsAsync(401, ct);
            return;
        }
        var result = await new UpdateAiAgentCommand(
            Guid.Parse(userId),
            req.Id,
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