using Domain.Aggregates.Chats;

namespace Application.Services;

public record MessageDto(
    string Content, 
    bool IsFromAi, 
    Guid MessageId,
    IReadOnlyList<FileAttachment>? FileAttachments = null,
    string? Base64Content = null
); 