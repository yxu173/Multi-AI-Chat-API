using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.AiProviders.CreateAiProvider;

public sealed class CreateAiProviderCommandHandler : ICommandHandler<CreateAiProviderCommand, Guid>
{
    private readonly IAiProviderRepository _providerRepository;

    public CreateAiProviderCommandHandler(IAiProviderRepository providerRepository)
    {
        _providerRepository = providerRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(CreateAiProviderCommand command, CancellationToken ct)
    {
        var aiProvider = AiProvider.Create(command.Name, command.Description);

        await _providerRepository.AddAsync(aiProvider);

        return Result.Success(aiProvider.Id);
    }
}