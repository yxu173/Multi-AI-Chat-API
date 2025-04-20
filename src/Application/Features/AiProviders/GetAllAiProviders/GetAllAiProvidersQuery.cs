using Application.Abstractions.Messaging;

namespace Application.Features.AiProviders.GetAllAiProviders;

public sealed record GetAllAiProvidersQuery() : IQuery<IReadOnlyList<AiProviderDto>>;