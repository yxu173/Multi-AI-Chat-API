using Domain.Aggregates.Chats;
using Domain.Enums;

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
    public string? Status { get; init; }

    public static MessageDto FromEntity(Message message, string? overrideThinkingContent = null, bool includeFunctionCallInfo = false)
    {
        ArgumentNullException.ThrowIfNull(message);
        
        var dto = new MessageDto(message.Content ?? string.Empty, message.IsFromAi, message.Id)
        {
            FileAttachments = message.FileAttachments?.ToList(),
            ThinkingContent = overrideThinkingContent ?? message.ThinkingContent,
            Status = message.Status.ToString() 
        };

        // TODO: Map FunctionCall and FunctionResponse from Message entity if it contains structured tool call/result info.
        // This requires Message entity to store this information in a parsable way if this DTO is to reflect it.
        // For now, FunctionCall and FunctionResponse on MessageDto will remain null unless set explicitly elsewhere
        // or if Message entity gets properties for these that can be mapped here.
        // If `includeFunctionCallInfo` is true, attempt to parse `message.Content` if it might contain tool info.
        // This is complex and depends on how Message.Content stores this.

        return dto;
    }
} 