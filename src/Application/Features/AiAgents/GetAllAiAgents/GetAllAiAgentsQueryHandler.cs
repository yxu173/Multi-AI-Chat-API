using Application.Abstractions.Messaging;
using Domain.Repositories;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using System.Text.Json;

namespace Application.Features.AiAgents.GetAllAiAgents;

public class GetAllAiAgentsQueryHandler : IQueryHandler<GetAllAiAgentsQuery, List<AiAgentResponse>>
{
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IPluginRepository _pluginRepository;

    public GetAllAiAgentsQueryHandler(
        IAiAgentRepository aiAgentRepository,
        IPluginRepository pluginRepository)
    {
        _aiAgentRepository = aiAgentRepository;
        _pluginRepository = pluginRepository;
    }

    public async Task<Result<List<AiAgentResponse>>> Handle(GetAllAiAgentsQuery request, CancellationToken cancellationToken)
    {
        var agents = await _aiAgentRepository.GetByUserIdAsync(request.UserId, cancellationToken);
        
        if (agents == null || !agents.Any())
        {
            return Result.Success(new List<AiAgentResponse>());
        }

        var allPlugins = await _pluginRepository.GetAllAsync();
        var pluginLookup = allPlugins.ToDictionary(p => p.Id, p => p.Name);

        var response = agents.Select(agent => new AiAgentResponse(
            Id: agent.Id,
            Name: agent.Name,
            Description: agent.Description,
            SystemInstructions: agent.ModelParameter.SystemInstructions,
            AiModelId: agent.ModelParameter.DefaultModel,
            AiModelName: agent.AiModel?.Name ?? "Unknown Model",
            IconUrl: agent.IconUrl,
            Categories: agent.Categories,
            AssignCustomModelParameters: agent.AssignCustomModelParameters,
            ModelParameters: agent.ModelParameter != null ? agent.ModelParameter.ToJson() : null,
            ProfilePictureUrl: agent.ProfilePictureUrl,
            Plugins: agent.AiAgentPlugins.Select(ap => new AgentPluginResponse(
                ap.PluginId,
                pluginLookup.TryGetValue(ap.PluginId, out var name) ? name : "Unknown Plugin",
                ap.Order,
                ap.IsActive
            )).ToList()
        )).ToList();

        return Result.Success(response);
    }
} 