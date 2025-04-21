using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiAgents.DeleteAiAgent;

public sealed class DeleteAiAgentCommandHandler : ICommandHandler<DeleteAiAgentCommand, bool>
{
    private readonly IAiAgentRepository _aiAgentRepository;

    public DeleteAiAgentCommandHandler(IAiAgentRepository aiAgentRepository)
    {
        _aiAgentRepository = aiAgentRepository;
    }

    public async Task<Result<bool>> Handle(DeleteAiAgentCommand request, CancellationToken cancellationToken)
    {
        var aiAgent = await _aiAgentRepository.GetByIdAsync(request.AiAgentId, cancellationToken);

        if (aiAgent == null || aiAgent.UserId != request.UserId)
        {
            return Result.Failure<bool>(Error.NotFound(
                "Ai Agent not found.",
                "The ai agent you are trying to delete does not exist."
            ));
        }
        
        await _aiAgentRepository.DeleteAsync(request.AiAgentId, cancellationToken);
        
        return Result.Success(true);
        
    }
}