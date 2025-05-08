using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.UserApiKey.CreateUserApiKey;

public sealed class CreateUserApiKeyCommandHandler : ICommandHandler<CreateUserApiKeyCommand, bool>
{
    private readonly IUserApiKeyRepository _userApiKeyRepository;

    public CreateUserApiKeyCommandHandler(IUserApiKeyRepository userApiKeyRepository)
    {
        _userApiKeyRepository = userApiKeyRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(CreateUserApiKeyCommand command, CancellationToken ct)
    {
        var userApiKey =
            Domain.Aggregates.Users.UserApiKey.Create(command.UserId, command.AiProviderId, command.ApiKey);
        await _userApiKeyRepository.AddAsync(userApiKey);

        return Result.Success(true);
    }
}