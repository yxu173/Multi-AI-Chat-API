using Application.Abstractions.Messaging;

namespace Application.Features.Chats.SendMessage;

public record SendMessageCommand(
    Guid ChatSessionId,
    Guid UserId,
    string? Content,
    bool EnableThinking = false,
    string? ImageSize = null,
    int? NumImages = null,
    string? OutputFormat = null,
    bool? EnableSafetyChecker = null,
    string? SafetyTolerance = null,
    bool EnableDeepSearch = false
) : ICommand; 