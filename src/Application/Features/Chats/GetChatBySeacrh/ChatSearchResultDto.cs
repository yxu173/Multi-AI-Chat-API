namespace Application.Features.Chats.GetChatBySeacrh;

public sealed record ChatSearchResultDto(
    Guid Id,
    string Title,
    IReadOnlyList<MessageSearchResultDto> Messages
);

public record MessageSearchResultDto(
    Guid Id,
    string Content,
    DateTime CreatedAt
);