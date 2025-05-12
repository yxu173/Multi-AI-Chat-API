using Application.Abstractions.Messaging;

namespace Application.Features.Admin.ProviderApiKeys.UpdateProviderApiKey;

public sealed record UpdateProviderApiKeyCommand(
    Guid ProviderApiKeyId,
    string? ApiSecret = null,
    string? Label = null,
    int? DailyQuota = null,
    bool? IsActive = null) : ICommand<bool>;
