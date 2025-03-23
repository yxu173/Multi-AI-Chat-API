using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernel;

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

    public async Task<Result<Guid>> Handle(CreateAiAgentCommand request, CancellationToken cancellationToken)
    {
       
        var model = await _aiModelRepository.GetByIdAsync(request.AiModelId);
        if (model == null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "AiAgent.ModelNotFound",
                $"AI Model with ID {request.AiModelId} not found."));
        }

        try
        {
            
            var agent = AiAgent.Create(
                request.UserId,
                request.Name,
                request.Description,
                request.AiModelId,
                request.SystemInstructions,
                request.IconUrl,
                request.Categories,
                request.AssignCustomModelParameters,
                request.ModelParameters,
                request.ProfilePictureUrl
            );

          
            if (request.Plugins != null && request.Plugins.Count > 0)
            {
                foreach (var plugin in request.Plugins)
                {
                    agent.AddPlugin(plugin.PluginId, plugin.Order, plugin.IsActive);
                }
            }

            
            await _aiAgentRepository.AddAsync(agent, cancellationToken);

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