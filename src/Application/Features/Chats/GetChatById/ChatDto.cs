namespace Application.Features.Chats.GetChatById;

public record ChatDto(
    Guid Id,
    string Title,
    DateTime CreatedAt,
    IReadOnlyList<MessageDto> Messages
);

public record MessageDto(
    Guid Id,
    string Content,
    bool IsFromAi,
    DateTime CreatedAt,
    IReadOnlyList<FileAttachmentDto> FileAttachments
);

public record FileAttachmentDto(
    Guid Id,
    string FileName,
    string FilePath
);