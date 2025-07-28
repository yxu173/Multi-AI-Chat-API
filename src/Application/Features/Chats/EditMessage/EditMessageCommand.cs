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
    double? Temperature = null,
    int? OutputToken = null
) : ICommand; 