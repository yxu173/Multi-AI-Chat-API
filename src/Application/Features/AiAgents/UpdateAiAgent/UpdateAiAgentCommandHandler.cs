using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;
using Domain.ValueObjects;

namespace Application.Features.AiAgents.UpdateAiAgent;

public class UpdateAiAgentCommandHandler : ICommandHandler<UpdateAiAgentCommand, bool>
{
    private readonly IAiAgentRepository _aiAgentRepository;

    public UpdateAiAgentCommandHandler(IAiAgentRepository aiAgentRepository)
    {
        _aiAgentRepository = aiAgentRepository;
    }

    public async Task<Result<bool>> Handle(UpdateAiAgentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var agent = await _aiAgentRepository.GetByIdAsync(command.AiAgentId, cancellationToken);

            if (agent == null)
            {
                return Result.Failure<bool>(Error.NotFound("AiAgent.NotFound", "AiAgent not found"));
            }

            if (agent.UserId != command.UserId)
            {
                return Result.Failure<bool>(Error.BadRequest("AiAgent.Unauthorized", "Unauthorized access to this AiAgent"));
            }

            ModelParameters? modelParameters = null;
            if (command.AssignCustomModelParameters == true)
            {
                modelParameters = ModelParameters.Create(
                    command.Temperature,
                    command.PresencePenalty,
                    command.FrequencyPenalty,
                    command.TopP,
                    command.TopK,
                    command.MaxTokens,
                    command.EnableThinking,
                    command.StopSequences,
                    command.PromptCaching,
                    command.ContextLimit,
                    command.SafetySettings
                );
            }

            agent.Update(
                command.Name,
                command.Description,
                command.AiModelId,
                command.SystemInstructions,
                command.IconUrl,
                command.Categories,
                command.AssignCustomModelParameters,
                modelParameters,
                command.ProfilePictureUrl
            );

            if (command.Plugins != null && command.Plugins.Any())
            {
                foreach (var plugin in command.Plugins)
                {
                    var existingPlugin = agent.AiAgentPlugins.FirstOrDefault(p => p.PluginId == plugin.PluginId);
                    
                    if (existingPlugin != null)
                    {
                        existingPlugin.UpdateOrder(plugin.Order);
                        existingPlugin.SetActive(plugin.IsActive);
                    }
                    else
                    {
                        agent.AddPlugin(plugin.PluginId, plugin.Order, plugin.IsActive);
                    }
                }
            }

            await _aiAgentRepository.UpdateAsync(agent, cancellationToken);
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            return Result.Failure<bool>(Error.Failure(
                "AiAgent.UpdateFailed",
                $"Error updating AI agent: {ex.Message}"));
        }
    }
} 