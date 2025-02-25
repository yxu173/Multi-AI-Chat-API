using Application.Abstractions.Messaging;

namespace Application.Features.Prompts.UpdatePrompt;

public sealed record UpdatePromptCommand(Guid PromptId,string Title,string Description, string Content) : ICommand<bool>;