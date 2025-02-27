using Application.Abstractions.Messaging;

namespace Application.Features.Prompts.CreatePrompt;

public sealed record CreatePromptCommand(
    Guid UserId,
    string Title,
    string Description,
    string Content,
    IEnumerable<string> Tags)
    : ICommand<Guid>;