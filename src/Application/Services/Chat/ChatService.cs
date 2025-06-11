using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using System.Threading;
using System.Threading.Tasks;
using Application.Services.Plugins;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using FastEndpoints;

namespace Application.Services.Chat;

public class ChatService
{
    private readonly SendUserMessageCommand _sendUserMessageCommand;
    private readonly EditUserMessageCommand _editUserMessageCommand;
    private readonly RegenerateAiResponseCommand _regenerateAiResponseCommand;
    private readonly PluginService _pluginService;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        SendUserMessageCommand sendUserMessageCommand,
        EditUserMessageCommand editUserMessageCommand,
        RegenerateAiResponseCommand regenerateAiResponseCommand,
        PluginService pluginService,
        ILogger<ChatService> logger)
    {
        _sendUserMessageCommand = sendUserMessageCommand ?? throw new ArgumentNullException(nameof(sendUserMessageCommand));
        _editUserMessageCommand = editUserMessageCommand ?? throw new ArgumentNullException(nameof(editUserMessageCommand));
        _regenerateAiResponseCommand = regenerateAiResponseCommand ?? throw new ArgumentNullException(nameof(regenerateAiResponseCommand));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        string? safetyTolerance = null,
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
        string? safetyTolerance = null,
        CancellationToken cancellationToken = default)
        => _editUserMessageCommand.ExecuteAsync(chatSessionId, userId, messageId, newContent, imageSize, numImages, outputFormat, enableSafetyChecker, safetyTolerance, cancellationToken);

    public Task RegenerateAiResponseAsync(
        Guid chatSessionId,
        Guid userId,
        Guid userMessageId,
        CancellationToken cancellationToken = default)
        => _regenerateAiResponseCommand.ExecuteAsync(chatSessionId, userId, userMessageId, cancellationToken);

    public async Task DeepSearchAndSendMessageAsync(
        Guid chatSessionId,
        Guid userId,
        string content,
        bool enableThinking,
        string? imageSize,
        int? numImages,
        string? outputFormat,
        bool? enableSafetyChecker,
        string? safetyTolerance,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await new DeepSearchStartedNotification(chatSessionId, "Starting deep search...").PublishAsync(Mode.WaitForNone, cancellationToken);

            var pluginId = new Guid("235979c5-cec1-4af2-9d61-6c1079c80be5"); // JinaDeepSearchPlugin ID
            var arguments = new JsonObject
            {
                ["query"] = content
            };

            var pluginResult = await _pluginService.ExecutePluginByIdAsync(pluginId, arguments, cancellationToken);

            if (pluginResult.Success)
            {
                await new DeepSearchResultsNotification(chatSessionId, pluginResult.Result).PublishAsync(Mode.WaitForNone, cancellationToken);

                var enhancedContent = $"{content}\n\nDeep Search Results:\n{pluginResult.Result}";

                await _sendUserMessageCommand.ExecuteAsync(chatSessionId, userId, enhancedContent,
                    enableThinking, imageSize, numImages, outputFormat, enableSafetyChecker, safetyTolerance, cancellationToken);
            }
            else
            {
                var errorMessage = $"Deep search failed: {pluginResult.ErrorMessage}";
                await new DeepSearchErrorNotification(chatSessionId, errorMessage).PublishAsync(Mode.WaitForNone, cancellationToken);
                await _sendUserMessageCommand.ExecuteAsync(chatSessionId, userId, content,
                    enableThinking, imageSize, numImages, outputFormat, enableSafetyChecker, safetyTolerance, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing deep search for chat {ChatSessionId}", chatSessionId);
            await new DeepSearchErrorNotification(chatSessionId, "Deep search execution failed").PublishAsync(Mode.WaitForNone, cancellationToken);
            
            await _sendUserMessageCommand.ExecuteAsync(chatSessionId, userId, content,
                enableThinking, imageSize, numImages, outputFormat, enableSafetyChecker, safetyTolerance, cancellationToken);
        }
    }
}