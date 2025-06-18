using Application.Abstractions.Messaging;

namespace Application.Features.Chats.DeepSearch;

public record DeepSearchCommand(
    Guid ChatSessionId,
    Guid UserId,
    string Content,
    bool EnableThinking,
    string? ImageSize,
    int? NumImages,
    string? OutputFormat,
    bool? EnableSafetyChecker,
    string? SafetyTolerance
) : ICommand; 