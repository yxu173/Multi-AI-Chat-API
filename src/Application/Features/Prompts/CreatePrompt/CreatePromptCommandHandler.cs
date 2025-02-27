using Application.Abstractions.Messaging;
using Domain.Aggregates.Prompts;
using Domain.Repositories;
using Domain.ValueObjects;
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
        var tags = request.Tags
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .Select(Tag.Create)
            .ToList();

        var promptTemplate = PromptTemplate.Create(
            request.UserId,
            request.Title,
            request.Description,
            request.Content,
            tags);

        var result = await _promptRepository.AddAsync(promptTemplate);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        return Result.Success(promptTemplate.Id);
    }
}