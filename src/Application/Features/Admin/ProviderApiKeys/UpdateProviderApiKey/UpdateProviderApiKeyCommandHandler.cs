using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Admin.ProviderApiKeys.UpdateProviderApiKey;

internal sealed class UpdateProviderApiKeyCommandHandler : ICommandHandler<UpdateProviderApiKeyCommand, bool>
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;

    public UpdateProviderApiKeyCommandHandler(IProviderApiKeyRepository providerApiKeyRepository)
    {
        _providerApiKeyRepository = providerApiKeyRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateProviderApiKeyCommand command, CancellationToken ct)
    {
        var providerApiKey = await _providerApiKeyRepository.GetByIdAsync(command.ProviderApiKeyId, ct);
        if (providerApiKey == null)
        {
            return Result.Failure<bool>(Error.NotFound(
                "ProviderApiKey.NotFound",
                $"Provider API key with ID {command.ProviderApiKeyId} does not exist"));
        }

        if (command.ApiSecret != null)
        {
            providerApiKey.UpdateSecret(command.ApiSecret);
        }

        if (command.Label != null)
        {
            providerApiKey.UpdateLabel(command.Label);
        }

        if (command.DailyQuota.HasValue)
        {
            providerApiKey.SetDailyQuota(command.DailyQuota.Value);
        }

        if (command.IsActive.HasValue)
        {
            providerApiKey.SetActive(command.IsActive.Value);
        }

        await _providerApiKeyRepository.UpdateAsync(providerApiKey, ct);

        return Result.Success(true);
    }
}
