using Application.Abstractions.Messaging;

namespace Application.Features.UserApiKey.UpdateUserApiKey;

public sealed record UpdateUserApiKeyCommand(Guid UserId, Guid UserApiKeyId, string ApiKey) : ICommand<bool>;