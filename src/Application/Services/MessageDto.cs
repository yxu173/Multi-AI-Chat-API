using Domain.Aggregates.Chats;
using System.Collections.Generic;

namespace Application.Services;

public record MessageDto(
    string Content, 
    bool IsFromAi, 
    Guid MessageId)
{
    public List<FileAttachment>? FileAttachments { get; init; }
} 