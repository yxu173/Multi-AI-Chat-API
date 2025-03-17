using Application.Abstractions.Messaging;

namespace Application.Features.Prompts.DeletePrompt;

public record DeletePromptCommand(Guid PromptId) : ICommand<bool>;
