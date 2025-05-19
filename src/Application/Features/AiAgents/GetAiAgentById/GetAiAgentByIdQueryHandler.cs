using Application.Abstractions.Messaging;
using Application.Features.AiAgents.GetAllAiAgents;
using Domain.Repositories;
using System.Text.Json;
using SharedKernal;

namespace Application.Features.AiAgents.GetAiAgentById;

public class GetAiAgentByIdQueryHandler : IQueryHandler<GetAiAgentByIdQuery, AiAgentResponse>
{
    private readonly IAiAgentRepository _aiAgentRepository;

    public GetAiAgentByIdQueryHandler(IAiAgentRepository aiAgentRepository)
    {
        _aiAgentRepository = aiAgentRepository;
    }

    public async Task<Result<AiAgentResponse>> ExecuteAsync(GetAiAgentByIdQuery query, CancellationToken ct)
    {
        var agent = await _aiAgentRepository.GetByIdAsync(query.AiAgentId, ct);

        if (agent == null)
        {
            return Result.Failure<AiAgentResponse>(Error.NotFound("AiAgent.NotFound", "AiAgent not found"));
        }

        if (agent.UserId != query.UserId)
        {
            return Result.Failure<AiAgentResponse>(Error.BadRequest("AiAgent.Unauthorized", "Unauthorized access to this AiAgent"));
        }

        var plugins = agent.AiAgentPlugins
            .Select(p => new AgentPluginResponse(
                p.PluginId,
                p.Plugin?.Name ?? "Unknown Plugin",
                p.IsActive))
            .ToList();

        var modelParametersJson = agent.ModelParameter != null
            ? JsonSerializer.Serialize(agent.ModelParameter)
            : null;

        var response = new AiAgentResponse(
            agent.Id,
            agent.Name,
            agent.Description,
            agent.ModelParameter.SystemInstructions,
            agent.ModelParameter.DefaultModel,
            agent.AiModel?.Name ?? "Unknown Model",
            agent.Categories.Select(c => c.ToString()).ToList(),
            agent.AssignCustomModelParameters,
            modelParametersJson,
            agent.ProfilePictureUrl,
            plugins);

        return Result.Success(response);
    }
} 