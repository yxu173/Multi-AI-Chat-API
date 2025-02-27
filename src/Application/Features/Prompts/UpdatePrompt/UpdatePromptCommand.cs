using Application.Abstractions.Messaging;

namespace Application.Features.Prompts.UpdatePrompt;

public sealed record UpdatePromptCommand(
    Guid PromptId,
    Guid UserId,
    string Title,
    string Description,
    string Content,
    IEnumerable<string> Tags) : ICommand<bool>;