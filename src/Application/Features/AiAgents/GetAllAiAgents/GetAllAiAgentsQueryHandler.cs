using Application.Abstractions.Messaging;
using Domain.Repositories;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Application.Features.AiAgents.GetAiAgentById;
using Domain.Enums;
using System.Collections.Generic;
using System.Linq;
using SharedKernal;

namespace Application.Features.AiAgents.GetAllAiAgents;

public class GetAllAiAgentsQueryHandler : IQueryHandler<GetAllAiAgentsQuery, GetAllAiAgentsGroupedByCategoryResponse>
{
    private readonly IAiAgentRepository _aiAgentRepository;

    public GetAllAiAgentsQueryHandler(IAiAgentRepository aiAgentRepository)
    {
        _aiAgentRepository = aiAgentRepository;
    }

    public async Task<Result<GetAllAiAgentsGroupedByCategoryResponse>> ExecuteAsync(GetAllAiAgentsQuery request, CancellationToken ct)
    {
        var agents = await _aiAgentRepository.GetByUserIdAsync(request.UserId, ct);

        var agentsByCategory = new Dictionary<string, List<GetAllAiAgentResponse>>();

        foreach (var category in Enum.GetValues<AgentCategories>())
        {
            var categoryName = category.ToString();
            var agentsInCategory = agents
                .Where(agent => agent.Categories != null && agent.Categories.Contains(category))
                .Select(agent => new GetAllAiAgentResponse(
                    Id: agent.Id,
                    Name: agent.Name,
                    Description: agent.Description,
                    ProfilePictureUrl: agent.ProfilePictureUrl
                )).ToList();

            agentsByCategory.Add(categoryName, agentsInCategory);
        }

        var response = new GetAllAiAgentsGroupedByCategoryResponse(agentsByCategory);
        return Result.Success(response);
    }
}