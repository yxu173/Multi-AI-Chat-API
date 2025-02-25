using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Prompts.UpdatePrompt;

public sealed class UpdatePromptCommandHandler : ICommandHandler<UpdatePromptCommand, bool>
{
    private readonly IPromptRepository _promptRepository;

    public UpdatePromptCommandHandler(IPromptRepository promptRepository)
    {
        _promptRepository = promptRepository;
    }

    public async Task<Result<bool>> Handle(UpdatePromptCommand request, CancellationToken cancellationToken)
    {
        var prompt = await _promptRepository.GetByIdAsync(request.PromptId);

        prompt.Value.Update(request.Title, request.Description, request.Content);

        var result = await _promptRepository.UpdateAsync(prompt.Value);
        if (result.IsFailure)
        {
            return Result.Failure<bool>(result.Error);
        }

        return Result.Success(true);
    }
}