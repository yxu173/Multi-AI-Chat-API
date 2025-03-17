using Application.Abstractions.Messaging;

namespace Application.Features.UserApiKey.CreateUserApiKey;

public sealed record CreateUserApiKeyCommand(Guid UserId, Guid AiProviderId, string ApiKey) : ICommand<bool>;