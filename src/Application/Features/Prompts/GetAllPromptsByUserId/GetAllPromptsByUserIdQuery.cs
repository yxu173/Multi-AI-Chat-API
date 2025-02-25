using Application.Abstractions.Messaging;

namespace Application.Features.Prompts.GetAllPromptsByUserId;

public record GetAllPromptsByUserIdQuery(Guid UserId) : IQuery<IEnumerable<PromptDto>>;