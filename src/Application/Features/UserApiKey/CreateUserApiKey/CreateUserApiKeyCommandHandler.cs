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

    public async Task<Result<bool>> Handle(CreateUserApiKeyCommand request, CancellationToken cancellationToken)
    {
        var userApiKey =
            Domain.Aggregates.Users.UserApiKey.Create(request.UserId, request.AiProviderId, request.ApiKey);
        await _userApiKeyRepository.AddAsync(userApiKey);

        return Result.Success(true);
    }
}