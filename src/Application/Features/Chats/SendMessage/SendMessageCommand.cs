using Application.Abstractions.Messaging;

namespace Application.Features.Chats.SendMessage;

public record SendMessageCommand(
    Guid ChatSessionId,
    Guid UserId,
    string? Content,
    List<Guid>? FileAttachmentIds = null,
    List<Guid>? ImageAttachmentIds = null,
    bool EnableThinking = false,
    string? ImageSize = null,
    int? NumImages = null,
    string? OutputFormat = null,
    bool EnableDeepSearch = false,
    double? Temperature = null,
    int? OutputToken = null
) : ICommand;