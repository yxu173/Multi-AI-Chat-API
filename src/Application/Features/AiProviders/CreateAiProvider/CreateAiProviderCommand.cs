using Application.Abstractions.Messaging;

namespace Application.Features.AiProviders.CreateAiProvider;

public sealed record CreateAiProviderCommand(string Name , string Description) : ICommand<Guid>;