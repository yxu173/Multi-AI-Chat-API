using Application.Abstractions.Messaging;
using Application.Features.AiProviders.GetAllAiProviders;

namespace Application.Features.AiProviders.GetAiProviderById;

public sealed record GetAiProviderByIdQuery(Guid Id) : IQuery<AiProviderDto>;