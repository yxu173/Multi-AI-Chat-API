using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Prompts.GetAllPromptsByUserId;

public class GetAllPromptsByUserIdQueryHandler : IQueryHandler<GetAllPromptsByUserIdQuery, IEnumerable<PromptDto>>
{
    private readonly IPromptRepository _promptRepository;

    public GetAllPromptsByUserIdQueryHandler(IPromptRepository promptRepository)
    {
        _promptRepository = promptRepository;
    }

    public async Task<Result<IEnumerable<PromptDto>>> ExecuteAsync(GetAllPromptsByUserIdQuery command,
        CancellationToken ct)
    {
        var prompts = await _promptRepository.GetAllPromptsByUserId(command.UserId);

        return Result.Success(prompts.Select(p => new PromptDto(p.Id, p.Title, p.Description, p.Content)));
    }
}