using Application.Abstractions.Messaging;

namespace Application.Features.AiProviders.DeleteAiProvider;

public sealed record DeleteAiProviderCommand(Guid Id) : ICommand<bool>;