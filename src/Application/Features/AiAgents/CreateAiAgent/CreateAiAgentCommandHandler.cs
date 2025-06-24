using Application.Abstractions.Messaging;
using Domain.Aggregates.AiAgents;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using Domain.ValueObjects;
using SharedKernal;

namespace Application.Features.AiAgents.CreateAiAgent;

public sealed class CreateAiAgentCommandHandler : ICommandHandler<CreateAiAgentCommand, Guid>
{
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IAiModelRepository _aiModelRepository;

    public CreateAiAgentCommandHandler(
        IAiAgentRepository aiAgentRepository,
        IAiModelRepository aiModelRepository)
    {
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        _aiModelRepository = aiModelRepository ?? throw new ArgumentNullException(nameof(aiModelRepository));
    }

    public async Task<Result<Guid>> ExecuteAsync(CreateAiAgentCommand request, CancellationToken ct)
    {
        var model = await _aiModelRepository.GetByIdAsync(request.DefaultModel);
        if (model == null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "AiAgent.ModelNotFound",
                $"AI Model with ID {request.DefaultModel} not found."));
        }

        try
        {
            ModelParameters? modelParameters = null;
            if (request.AssignCustomModelParameters)
            {
                modelParameters = ModelParameters.Create(request.DefaultModel,
                    request.SystemInstructions,
                    temperature: request.Temperature,
                    presencePenalty: request.PresencePenalty,
                    frequencyPenalty: request.FrequencyPenalty,
                    topP: request.TopP,
                    topK: request.TopK,
                    maxTokens: request.MaxTokens,
                    promptCaching: request.PromptCaching,
                    contextLimit: request.ContextLimit, safetySettings: request.SafetySettings);
            }
            
            var agent = AiAgent.Create(
                request.UserId,
                request.Name,
                request.Description,
                request.Categories,
                request.AssignCustomModelParameters,
                modelParameters,
                request.ProfilePictureUrl,
                request.DefaultModel
            );

            if (request.Plugins != null && request.Plugins.Count > 0)
            {
                foreach (var plugin in request.Plugins)
                {
                    agent.AddPlugin(plugin.PluginId, plugin.IsActive);
                }
            }
            
            await _aiAgentRepository.AddAsync(agent, ct);

            return Result.Success(agent.Id);
        }
        catch (Exception ex)
        {
            return Result.Failure<Guid>(Error.Failure(
                "AiAgent.CreationFailed",
                $"Error creating AI agent: {ex.Message}"));
        }
    }
}