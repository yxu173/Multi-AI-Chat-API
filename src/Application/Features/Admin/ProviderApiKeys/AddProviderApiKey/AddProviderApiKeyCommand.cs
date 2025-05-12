using Application.Abstractions.Messaging;

namespace Application.Features.Admin.ProviderApiKeys.AddProviderApiKey;

public sealed record AddProviderApiKeyCommand(
    Guid AiProviderId, 
    string ApiSecret, 
    string Label, 
    Guid CreatedByUserId,
    int DailyQuota = 1000) : ICommand<Guid>;
