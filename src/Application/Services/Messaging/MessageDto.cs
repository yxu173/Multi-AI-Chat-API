using Domain.Aggregates.Chats;

namespace Application.Services.Messaging;

public record FunctionCall(
    string Name,
    string Arguments,
    string? Id = null
);

public record FunctionResponse(
    string Name,
    string Content,
    string FunctionCallId
);

public record MessageDto(
    string Content, 
    bool IsFromAi, 
    Guid MessageId)
{
    public List<FileAttachment>? FileAttachments { get; init; }
    public string? ThinkingContent { get; init; }
    public FunctionCall? FunctionCall { get; init; }
    public FunctionResponse? FunctionResponse { get; init; }
} 