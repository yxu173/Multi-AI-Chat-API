using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.Chat;

public class ChatService
{
    private readonly SendUserMessageCommand _sendUserMessageCommand;
    private readonly EditUserMessageCommand _editUserMessageCommand;
    private readonly RegenerateAiResponseCommand _regenerateAiResponseCommand;

    public ChatService(
        SendUserMessageCommand sendUserMessageCommand,
        EditUserMessageCommand editUserMessageCommand,
        RegenerateAiResponseCommand regenerateAiResponseCommand)
    {
        _sendUserMessageCommand = sendUserMessageCommand ?? throw new ArgumentNullException(nameof(sendUserMessageCommand));
        _editUserMessageCommand = editUserMessageCommand ?? throw new ArgumentNullException(nameof(editUserMessageCommand));
        _regenerateAiResponseCommand = regenerateAiResponseCommand ?? throw new ArgumentNullException(nameof(regenerateAiResponseCommand));
    }

    public Task SendUserMessageAsync(
        Guid chatSessionId,
        Guid userId,
        string content,
        bool enableThinking = false,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        int? safetyTolerance = null,
        CancellationToken cancellationToken = default)
        => _sendUserMessageCommand.ExecuteAsync(chatSessionId, userId, content, enableThinking, imageSize, numImages, outputFormat, enableSafetyChecker, safetyTolerance, cancellationToken);

    public Task EditUserMessageAsync(
        Guid chatSessionId,
        Guid userId,
        Guid messageId,
        string newContent,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        int? safetyTolerance = null,
        CancellationToken cancellationToken = default)
        => _editUserMessageCommand.ExecuteAsync(chatSessionId, userId, messageId, newContent, imageSize, numImages, outputFormat, enableSafetyChecker, safetyTolerance, cancellationToken);

    public Task RegenerateAiResponseAsync(
        Guid chatSessionId,
        Guid userId,
        Guid userMessageId,
        CancellationToken cancellationToken = default)
        => _regenerateAiResponseCommand.ExecuteAsync(chatSessionId, userId, userMessageId, cancellationToken);
}