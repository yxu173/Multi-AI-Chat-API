using System;

namespace Web.Api.Contracts.Admin;

public record ProviderApiKeyResponse(
    Guid Id,
    Guid AiProviderId,
    string Label,
    string MaskedSecret,
    bool IsActive,
    int MaxRequestsPerDay,
    int UsageCountToday,
    DateTime CreatedAt,
    DateTime? LastUsedTimestamp
);

public record AddProviderApiKeyRequest(
    Guid AiProviderId,
    string ApiSecret,
    string Label,
    int MaxRequestsPerDay = 1000
);

public record UpdateProviderApiKeyRequest(
    string? ApiSecret = null,
    string? Label = null,
    int? MaxRequestsPerDay = null,
    bool? IsActive = null
);
