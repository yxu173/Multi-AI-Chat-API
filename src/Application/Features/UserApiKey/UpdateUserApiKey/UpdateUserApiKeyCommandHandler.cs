using Application.Abstractions.Messaging;
using Domain.DomainErrors;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.UserApiKey.UpdateUserApiKey;

public sealed class UpdateUserApiKeyCommandHandler : ICommandHandler<UpdateUserApiKeyCommand, bool>
{
    private readonly IUserApiKeyRepository _userApiKeyRepository;

    public UpdateUserApiKeyCommandHandler(IUserApiKeyRepository userApiKeyRepository)
    {
        _userApiKeyRepository = userApiKeyRepository;
    }

    public async Task<Result<bool>> Handle(UpdateUserApiKeyCommand request, CancellationToken cancellationToken)
    {
        var apiKey = await _userApiKeyRepository.GetByIdAsync(request.UserApiKeyId);
        if (apiKey.UserId != request.UserId)
        {
            return Result.Failure<bool>(ApiKeyErrors.ValidUserId);
        }

        apiKey.UpdateLastUsed();
        apiKey.UpdateLastUsed();

        await _userApiKeyRepository.UpdateAsync(apiKey);

        return Result.Success(true);
    }
}