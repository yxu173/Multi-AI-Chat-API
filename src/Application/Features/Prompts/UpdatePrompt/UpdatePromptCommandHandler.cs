using Application.Abstractions.Messaging;
using Domain.DomainErrors;
using Domain.Repositories;
using Domain.ValueObjects;
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
        var promptResult = await _promptRepository.GetByIdAsync(request.PromptId);
        if (promptResult.IsFailure)
            return Result.Failure<bool>(promptResult.Error);

        var prompt = promptResult.Value;

        if (prompt.UserId != request.UserId)
            return Result.Failure<bool>(PromptTemplateErrors.UserIsNotAuthorized);

        prompt.Update(request.Title, request.Content,request.Description);

        var tags = request.Tags.Select(Tag.Create).ToList();
        prompt.UpdateTags(tags);

        var result = await _promptRepository.UpdateAsync(prompt);
        if (result.IsFailure)
            return Result.Failure<bool>(result.Error);

        return Result.Success(true);
    }
}