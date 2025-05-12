using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;

namespace Application.Features.Admin.ProviderApiKeys.GetProviderApiKeys;

public sealed record GetProviderApiKeysQuery(
    Guid? ProviderId = null) : IQuery<IReadOnlyList<ProviderApiKey>>;
