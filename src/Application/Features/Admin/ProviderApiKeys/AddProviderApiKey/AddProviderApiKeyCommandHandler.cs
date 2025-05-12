using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Admin.ProviderApiKeys.AddProviderApiKey;

internal sealed class AddProviderApiKeyCommandHandler : ICommandHandler<AddProviderApiKeyCommand, Guid>
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;
    private readonly IAiProviderRepository _aiProviderRepository;

    public AddProviderApiKeyCommandHandler(
        IProviderApiKeyRepository providerApiKeyRepository,
        IAiProviderRepository aiProviderRepository)
    {
        _providerApiKeyRepository = providerApiKeyRepository;
        _aiProviderRepository = aiProviderRepository;
    }

    public async Task<Result<System.Guid>> ExecuteAsync(AddProviderApiKeyCommand command, CancellationToken ct)
    {
        var providerExists = await _aiProviderRepository.ExistsAsync(command.AiProviderId);
        if (!providerExists)
        {
            return Result.Failure<System.Guid>(Error.NotFound(
                "ProviderApiKey.ProviderNotFound",
                $"Provider with ID {command.AiProviderId} does not exist"));
        }

        var providerApiKey = ProviderApiKey.Create(
            command.AiProviderId,
            command.ApiSecret, 
            command.Label,
            command.CreatedByUserId,
            command.DailyQuota);

        await _providerApiKeyRepository.AddAsync(providerApiKey, ct);

        return Result.Success(providerApiKey.Id);
    }
}
