using System;

namespace Web.Api.Contracts.Admin;

public record ProviderApiKeyResponse(
    Guid Id,
    Guid AiProviderId,
    string Label,
    string MaskedSecret,
    bool IsActive,
    int DailyQuota,
    int DailyUsage,
    DateTime CreatedAt,
    DateTime? LastUsed
);

public record AddProviderApiKeyRequest(
    Guid AiProviderId,
    string ApiSecret,
    string Label,
    int DailyQuota = 1000
);

public record UpdateProviderApiKeyRequest(
    string? ApiSecret = null,
    string? Label = null,
    int? DailyQuota = null,
    bool? IsActive = null
);
