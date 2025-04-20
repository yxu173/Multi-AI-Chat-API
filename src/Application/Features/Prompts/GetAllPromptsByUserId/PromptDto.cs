namespace Application.Features.Prompts.GetAllPromptsByUserId;

public record PromptDto(Guid Id, string Title, string Description, string Content);