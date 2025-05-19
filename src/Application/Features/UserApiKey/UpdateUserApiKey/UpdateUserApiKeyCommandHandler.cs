using Application.Abstractions.Messaging;
using Domain.DomainErrors;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.UserApiKey.UpdateUserApiKey;

public sealed class UpdateUserApiKeyCommandHandler : ICommandHandler<UpdateUserApiKeyCommand, bool>
{
    private readonly IUserApiKeyRepository _userApiKeyRepository;

    public UpdateUserApiKeyCommandHandler(IUserApiKeyRepository userApiKeyRepository)
    {
        _userApiKeyRepository = userApiKeyRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateUserApiKeyCommand command, CancellationToken ct)
    {
        var apiKey = await _userApiKeyRepository.GetByIdAsync(command.UserApiKeyId);
        if (apiKey.UserId != command.UserId)
        {
            return Result.Failure<bool>(ApiKeyErrors.ValidUserId);
        }

        apiKey.UpdateLastUsed();
        apiKey.UpdateLastUsed();

        await _userApiKeyRepository.UpdateAsync(apiKey);

        return Result.Success(true);
    }
}