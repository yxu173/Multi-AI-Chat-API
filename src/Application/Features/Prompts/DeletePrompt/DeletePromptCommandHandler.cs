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

    public async Task<Result<bool>> Handle(DeletePromptCommand request, CancellationToken cancellationToken)
    {
        var result = await _promptRepository.DeleteAsync(request.PromptId);
        if (result.IsFailure)
        {
            return Result.Failure<bool>(result.Error);
        }

        return Result.Success(true);
    }
}