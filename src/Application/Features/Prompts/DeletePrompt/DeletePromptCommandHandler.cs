using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Prompts.DeletePrompt;

public class DeletePromptCommandHandler : ICommandHandler<DeletePromptCommand, bool>
{
    private readonly IPromptRepository _promptRepository;

    public DeletePromptCommandHandler(IPromptRepository promptRepository)
    {
        _promptRepository = promptRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(DeletePromptCommand command, CancellationToken ct)
    {
        var result = await _promptRepository.DeleteAsync(command.PromptId);
        if (result.IsFailure)
        {
            return Result.Failure<bool>(result.Error);
        }

        return Result.Success(true);
    }
}