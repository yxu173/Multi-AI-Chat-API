using Application.Abstractions.Messaging;

namespace Application.Features.Chats.EditMessage;

public record EditMessageCommand(
    Guid ChatSessionId,
    Guid UserId,
    Guid MessageId,
    string NewContent,
    string? ImageSize = null,
    int? NumImages = null,
    string? OutputFormat = null,
    bool? EnableSafetyChecker = null,
    string? SafetyTolerance = null
) : ICommand; 