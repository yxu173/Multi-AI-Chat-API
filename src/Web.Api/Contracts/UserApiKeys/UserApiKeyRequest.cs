namespace Web.Api.Contracts.UserApiKeys;

public sealed record UserApiKeyRequest(
    Guid AiProviderId,
    string ApiKey
    );