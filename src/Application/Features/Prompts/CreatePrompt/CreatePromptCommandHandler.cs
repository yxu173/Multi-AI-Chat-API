using Application.Abstractions.Messaging;
using Domain.Aggregates.Prompts;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Prompts.CreatePrompt;

public sealed class CreatePromptCommandHandler : ICommandHandler<CreatePromptCommand, Guid>
{
    private readonly IPromptRepository _promptRepository;

    public CreatePromptCommandHandler(IPromptRepository promptRepository)
    {
        _promptRepository = promptRepository;
    }

    public async Task<Result<Guid>> Handle(CreatePromptCommand request, CancellationToken cancellationToken)
    {
        var promptTemplate = PromptTemplate.Create(request.UserId, request.Title, request.Description, request.Content);
        var result = await _promptRepository.AddAsync(promptTemplate.Value);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        return Result.Success(promptTemplate.Value.Id);
    }
}