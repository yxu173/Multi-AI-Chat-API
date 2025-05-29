using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiAgents.DeleteAiAgent;

public sealed class DeleteAiAgentCommandHandler : ICommandHandler<DeleteAiAgentCommand, bool>
{
    private readonly IAiAgentRepository _aiAgentRepository;

    public DeleteAiAgentCommandHandler(IAiAgentRepository aiAgentRepository)
    {
        _aiAgentRepository = aiAgentRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(DeleteAiAgentCommand request, CancellationToken ct)
    {
        var aiAgent = await _aiAgentRepository.GetByIdAsync(request.AiAgentId, ct);

        if (aiAgent == null || aiAgent.UserId != request.UserId)
        {
            return Result.Failure<bool>(Error.NotFound(
                "Ai Agent not found.",
                "The ai agent you are trying to delete does not exist."
            ));
        }
        
        await _aiAgentRepository.DeleteAsync(request.AiAgentId, ct);
        
        return Result.Success(true);
        
    }
}