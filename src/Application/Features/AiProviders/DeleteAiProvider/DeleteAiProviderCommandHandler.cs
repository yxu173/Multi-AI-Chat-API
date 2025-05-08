using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.AiProviders.DeleteAiProvider;

public sealed class DeleteAiProviderCommandHandler : ICommandHandler<DeleteAiProviderCommand, bool>
{
    private readonly IAiProviderRepository _aiProviderRepository;

    public DeleteAiProviderCommandHandler(IAiProviderRepository aiProviderRepository)
    {
        _aiProviderRepository = aiProviderRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(DeleteAiProviderCommand command, CancellationToken ct)
    {
        var result = await _aiProviderRepository.DeleteAsync(command.Id);
        return Result.Success(result);
    }
}