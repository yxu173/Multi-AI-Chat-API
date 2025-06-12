using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Domain.Repositories;
using Domain.Aggregates.Chats;
using Application.Features.Chats.GetChatById;
using Application.Features.Chats.SendMessage;
using Application.Features.Chats.EditMessage;
using Application.Features.Chats.RegenerateResponse;
using Application.Features.Chats.DeepSearch;
using FastEndpoints;
using System.Diagnostics;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly ILogger<ChatHub> _logger;

    private static readonly ActivitySource ActivitySource = new("Web.Api.Hubs.ChatHub", "1.0.0");
    private const int MaxClientFileSize = 10 * 1024 * 1024;

    public ChatHub(
        IFileAttachmentRepository fileAttachmentRepository,
        ILogger<ChatHub> logger)
    {
        _fileAttachmentRepository = fileAttachmentRepository ??
                                    throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }


    public override async Task OnConnectedAsync()
    {
        using var activity = ActivitySource.StartActivity(nameof(OnConnectedAsync));
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        activity?.SetTag("user.id", userId);
        activity?.SetTag("signalr.connection_id", Context.ConnectionId);
        _logger.LogInformation("User {UserId} connected to chat hub with ConnectionId {ConnectionId}", userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        using var activity = ActivitySource.StartActivity(nameof(OnDisconnectedAsync));
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        activity?.SetTag("user.id", userId);
        activity?.SetTag("signalr.connection_id", Context.ConnectionId);
        if (exception != null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
            _logger.LogInformation(exception, "User {UserId} disconnected from chat hub with ConnectionId {ConnectionId}. Reason: {Reason}", userId, Context.ConnectionId, exception.Message);
        }
        else
        {
            _logger.LogInformation("User {UserId} disconnected from chat hub with ConnectionId {ConnectionId} (Normal disconnection)", userId, Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChatSession(string chatSessionId)
    {
        using var activity = ActivitySource.StartActivity(nameof(JoinChatSession));
        activity?.SetTag("chat_session.id", chatSessionId);
        activity?.SetTag("user.id", Context.UserIdentifier);
        activity?.SetTag("signalr.connection_id", Context.ConnectionId);

        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Chat session ID is empty.");
                _logger.LogWarning("JoinChatSession called with empty chat session ID");
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid chat session ID format.");
                _logger.LogWarning("JoinChatSession called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, chatSessionId);
            _logger.LogInformation("User {UserId} joined chat session {ChatSessionId}", Context.UserIdentifier, chatSessionId);
            activity?.AddEvent(new ActivityEvent("Added to SignalR group."));

            try
            {
                using var fetchHistoryActivity = ActivitySource.StartActivity("FetchChatHistory");
                fetchHistoryActivity?.SetTag("chat_session.guid", chatGuid.ToString());
                var chatResult = await new GetChatByIdQuery(chatGuid).ExecuteAsync();

                if (chatResult.IsSuccess)
                {
                    fetchHistoryActivity?.SetTag("fetch_history.success", true);
                    await Clients.Caller.SendAsync("ReceiveChatHistory", chatResult.Value);
                    activity?.AddEvent(new ActivityEvent("Chat history sent to caller."));
                }
                else
                {
                    fetchHistoryActivity?.SetTag("fetch_history.success", false);
                    fetchHistoryActivity?.SetTag("fetch_history.error", chatResult.Error.Description ?? "Unknown error");
                    _logger.LogWarning("Failed to fetch chat history for session {ChatSessionId}. Error: {Error}", chatSessionId, chatResult.Error?.Description);
                    await Clients.Caller.SendAsync("Error", $"Joined chat but failed to load history: {chatResult.Error?.Description}");
                }
            }
            catch (Exception historyEx)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Error loading chat history.");
                activity?.AddException(historyEx);
                _logger.LogError(historyEx, "Error loading chat history for session {ChatSessionId}", chatSessionId);
                await Clients.Caller.SendAsync("Error", $"Joined chat but failed to load history: {historyEx.Message}");
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error joining chat session.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error joining chat session {ChatSessionId}", chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to join chat session: {ex.Message}");
        }
    }

    public async Task SendMessage(
        string chatSessionId,
        string content,
        bool enableThinking = false,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        string? safetyTolerance = null,
        bool enableDeepSearch = false)
    {
        try
        {
            if (!Guid.TryParse(chatSessionId, out var chatSessionGuid))
            {
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID");
                return;
            }

            var userIdValue = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                await Clients.Caller.SendAsync("Error", "User not authenticated or invalid ID format");
                return;
            }

            if (enableDeepSearch)
            {
                var deepSearchCommand = new DeepSearchCommand(
                    chatSessionGuid,
                    userId,
                    content,
                    enableThinking,
                    imageSize,
                    numImages,
                    outputFormat,
                    enableSafetyChecker,
                    safetyTolerance
                );
                await deepSearchCommand.ExecuteAsync();
            }
            else
            {
                var command = new SendMessageCommand(
                    chatSessionGuid,
                    userId,
                    content,
                    enableThinking,
                    imageSize,
                    numImages,
                    outputFormat,
                    enableSafetyChecker,
                    safetyTolerance
                );

                await command.ExecuteAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message in chat {ChatSessionId}", chatSessionId);
            await Clients.Caller.SendAsync("Error", "Failed to send message");
        }
    }

    /// <summary>
    /// Sends a message with file attachments to the AI
    /// </summary>
    public async Task SendMessageWithAttachments(
        string chatSessionId,
        string content,
        List<Guid> fileAttachmentIds,
        bool enableThinking = false,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        string? safetyTolerance = null
    )
    {
        using var activity = ActivitySource.StartActivity(nameof(SendMessageWithAttachments));
        activity?.SetTag("chat_session.id", chatSessionId);
        activity?.SetTag("user.id", Context.UserIdentifier);
        activity?.SetTag("message.content_length", content?.Length ?? 0);
        activity?.SetTag("message.attachment_count", fileAttachmentIds?.Count ?? 0);

        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Chat session ID is empty.");
                _logger.LogWarning("SendMessageWithAttachments called with empty chat session ID");
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid chat session ID format.");
                _logger.LogWarning("SendMessageWithAttachments called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            if (Context.UserIdentifier == null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "User identifier is null.");
                _logger.LogWarning("Method called with null user identifier");
                await Clients.Caller.SendAsync("Error", "User not authenticated");
                return;
            }
            if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid user identifier format.");
                _logger.LogWarning("Method called with invalid user identifier");
                await Clients.Caller.SendAsync("Error", "Invalid user identifier");
                return;
            }

            string processedContent = content ?? string.Empty;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                using var processAttachmentsActivity = ActivitySource.StartActivity("ProcessAttachmentsInHub");
                processedContent = await ProcessFileAttachmentsAsync(processedContent, fileAttachmentIds);
                processAttachmentsActivity?.SetTag("content_length_after_processing", processedContent.Length);
            }

            var command = new SendMessageCommand(
                chatGuid,
                userId,
                processedContent,
                enableThinking,
                imageSize,
                numImages,
                outputFormat,
                enableSafetyChecker,
                safetyTolerance);
            
            await command.ExecuteAsync();
            activity?.AddEvent(new ActivityEvent("Message with attachments sent via command."));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error sending message with attachments.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error sending message with attachments in session {ChatSessionId}", chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Edit an existing message
    /// </summary>
    public async Task EditMessage(string chatSessionId, string messageId, string newContent)
    {
        using var activity = ActivitySource.StartActivity(nameof(EditMessage));
        activity?.SetTag("chat_session.id", chatSessionId);
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("user.id", Context.UserIdentifier);
        activity?.SetTag("message.new_content_length", newContent?.Length ?? 0);

        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Chat session ID is empty.");
                _logger.LogWarning("EditMessage called with empty chat session ID");
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid chat session ID format.");
                _logger.LogWarning("EditMessage called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            if (string.IsNullOrWhiteSpace(messageId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Message ID is empty.");
                _logger.LogWarning("EditMessage called with empty message ID");
                await Clients.Caller.SendAsync("Error", "Message ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(messageId, out Guid messageGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid message ID format.");
                _logger.LogWarning("EditMessage called with invalid message ID format");
                await Clients.Caller.SendAsync("Error", "Invalid message ID format");
                return;
            }

            if (string.IsNullOrWhiteSpace(newContent))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "New message content empty.");
                _logger.LogWarning("EditMessage called with empty new content for message {MessageId} in session {ChatSessionId}", messageId, chatSessionId);
                await Clients.Caller.SendAsync("Error", "Message content cannot be empty");
                return;
            }

            if (Context.UserIdentifier == null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "User identifier is null.");
                _logger.LogWarning("Method called with null user identifier");
                await Clients.Caller.SendAsync("Error", "User not authenticated");
                return;
            }
            if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid user identifier format.");
                _logger.LogWarning("Method called with invalid user identifier");
                await Clients.Caller.SendAsync("Error", "Invalid user identifier");
                return;
            }

            var command = new EditMessageCommand(chatGuid, userId, messageGuid, newContent);
            await command.ExecuteAsync();
            activity?.AddEvent(new ActivityEvent("Edit message request sent to ChatService."));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error editing message.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error editing message {MessageId} in session {ChatSessionId}", messageId, chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
        }
    }

    /// <summary>
    /// Edit a message with file attachments
    /// </summary>
    public async Task EditMessageWithAttachments(string chatSessionId, string messageId, string newContent,
        List<Guid> fileAttachmentIds)
    {
        using var activity = ActivitySource.StartActivity(nameof(EditMessageWithAttachments));
        activity?.SetTag("chat_session.id", chatSessionId);
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("user.id", Context.UserIdentifier);
        activity?.SetTag("message.new_content_length", newContent?.Length ?? 0);
        activity?.SetTag("message.attachment_count", fileAttachmentIds?.Count ?? 0);

        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Chat session ID is empty.");
                _logger.LogWarning("EditMessageWithAttachments called with empty chat session ID");
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid chat session ID format.");
                _logger.LogWarning("EditMessageWithAttachments called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            if (string.IsNullOrWhiteSpace(messageId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Message ID is empty.");
                _logger.LogWarning("EditMessageWithAttachments called with empty message ID");
                await Clients.Caller.SendAsync("Error", "Message ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(messageId, out Guid messageGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid message ID format.");
                _logger.LogWarning("EditMessageWithAttachments called with invalid message ID format");
                await Clients.Caller.SendAsync("Error", "Invalid message ID format");
                return;
            }

            if (Context.UserIdentifier == null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "User identifier is null.");
                _logger.LogWarning("Method called with null user identifier");
                await Clients.Caller.SendAsync("Error", "User not authenticated");
                return;
            }
            if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid user identifier format.");
                _logger.LogWarning("Method called with invalid user identifier");
                await Clients.Caller.SendAsync("Error", "Invalid user identifier");
                return;
            }

            string processedContent = newContent ?? string.Empty;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                using var processAttachmentsActivity = ActivitySource.StartActivity("ProcessAttachmentsInHub");
                processedContent = await ProcessFileAttachmentsAsync(processedContent, fileAttachmentIds);
                processAttachmentsActivity?.SetTag("content_length_after_processing", processedContent.Length);
            }

            var command = new EditMessageCommand(chatGuid, userId, messageGuid, processedContent);
            await command.ExecuteAsync();
            activity?.AddEvent(new ActivityEvent("Edit message with attachments request sent to ChatService."));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error editing message with attachments.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error editing message {MessageId} with attachments in session {ChatSessionId}", messageId, chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all file attachments for a message
    /// </summary>
    public async Task GetMessageAttachments(string messageId)
    {
        using var activity = ActivitySource.StartActivity(nameof(GetMessageAttachments));
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("user.id", Context.UserIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Message ID is empty.");
                _logger.LogWarning("GetMessageAttachments called with empty message ID");
                await Clients.Caller.SendAsync("Error", "Message ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(messageId, out Guid messageGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid message ID format.");
                _logger.LogWarning("GetMessageAttachments called with invalid message ID format");
                await Clients.Caller.SendAsync("Error", "Invalid message ID format");
                return;
            }

            var attachments = await _fileAttachmentRepository.GetByMessageIdAsync(messageGuid);
            activity?.SetTag("attachments.count", attachments.Count);

            foreach (var attachment in attachments)
            {
                await Clients.Caller.SendAsync("ReceiveFile", new
                {
                    id = attachment.Id,
                    messageId = attachment.MessageId,
                    fileName = attachment.FileName,
                    contentType = attachment.ContentType,
                    fileType = attachment.FileType.ToString(),
                    fileSize = attachment.FileSize,
                    url = $"/api/file/{attachment.Id}"
                });
            }
            activity?.AddEvent(new ActivityEvent("Attachments sent to caller.", tags: new ActivityTagsCollection { { "count", attachments.Count } }));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error retrieving message attachments.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error retrieving message attachments for message {MessageId}", messageId);
            await Clients.Caller.SendAsync("Error", $"Failed to get message attachments: {ex.Message}");
        }
    }

    /// <summary>
    /// Regenerates the AI response for a given user message.
    /// </summary>
    public async Task RegenerateResponse(string chatSessionId, string userMessageId)
    {
        using var activity = ActivitySource.StartActivity(nameof(RegenerateResponse));
        activity?.SetTag("chat_session.id", chatSessionId);
        activity?.SetTag("message.id_to_regenerate_for", userMessageId);
        activity?.SetTag("user.id", Context.UserIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Chat session ID is empty.");
                _logger.LogWarning("RegenerateResponse called with empty chat session ID");
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid chat session ID format.");
                _logger.LogWarning("RegenerateResponse called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            if (string.IsNullOrWhiteSpace(userMessageId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "User message ID is empty.");
                _logger.LogWarning("RegenerateResponse called with empty user message ID");
                await Clients.Caller.SendAsync("Error", "User message ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(userMessageId, out Guid userMessageGuid))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid user message ID format.");
                _logger.LogWarning("RegenerateResponse called with invalid user message ID format");
                await Clients.Caller.SendAsync("Error", "Invalid user message ID format");
                return;
            }

            if (Context.UserIdentifier == null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "User identifier is null.");
                _logger.LogWarning("Method called with null user identifier");
                await Clients.Caller.SendAsync("Error", "User not authenticated");
                return;
            }
            if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid user identifier format.");
                _logger.LogWarning("Method called with invalid user identifier");
                await Clients.Caller.SendAsync("Error", "Invalid user identifier");
                return;
            }

            var command = new RegenerateResponseCommand(chatGuid, userId, userMessageGuid);
            await command.ExecuteAsync();
            activity?.AddEvent(new ActivityEvent("Regenerate response request sent to ChatService."));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error regenerating response.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error regenerating response for message {UserMessageId} in session {ChatSessionId}", userMessageId, chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to regenerate response: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes file attachments and integrates them into the message content
    /// </summary>
    private async Task<string> ProcessFileAttachmentsAsync(string content, List<Guid> fileAttachmentIds)
    {
        // This is a private helper method. Activities within it will be parented to the calling public method's activity.
        // So, starting a new top-level activity here might be redundant unless it represents a very distinct, long-running sub-operation.
        // For now, let operations within be part of the caller's span. Logging within will be correlated.
        var processedContent = content ?? string.Empty;

        foreach (var fileId in fileAttachmentIds)
        {
            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId);
            if (fileAttachment == null)
            {
                _logger.LogWarning("File attachment with ID {FileId} not found in repository during content processing.", fileId);
                processedContent += $"\n[Attachment metadata not found for ID: {fileId}]";
                continue;
            }

            if (string.IsNullOrWhiteSpace(fileAttachment.FilePath))
            {
                _logger.LogError("File attachment with ID {FileId} has an empty or null FilePath during content processing.", fileId);
                processedContent += $"\n[Attachment {fileAttachment.FileName} has invalid path information]";
                continue;
            }

            if (!System.IO.File.Exists(fileAttachment.FilePath))
            {
                _logger.LogWarning("File attachment {FileName} (ID: {FileId}) not found at path: {FilePath} during content processing",
                                 fileAttachment.FileName, fileId, fileAttachment.FilePath);
                processedContent += $"\n[Attachment {fileAttachment.FileName} not found on disk]";
                continue;
            }

            if (fileAttachment.FileSize > MaxClientFileSize)
            {
                _logger.LogWarning("File attachment {FileName} (ID: {FileId}, Size: {FileSize}) exceeds max client size {MaxFileSize} and was skipped during content processing.",
                                 fileAttachment.FileName, fileId, fileAttachment.FileSize, MaxClientFileSize);
                processedContent +=
                    $"\n[Attachment {fileAttachment.FileName} skipped: exceeds size limit of {MaxClientFileSize / (1024 * 1024)}MB]";
                continue;
            }

            try
            {
                if (fileAttachment.FileType == FileType.Image)
                {
                    processedContent += $"\n\n<image:{fileId}>\n\n";
                }
                else
                {
                    processedContent += $"\n\n<file:{fileId}:{fileAttachment.FileName}>\n\n";
                }
            }
            catch (Exception ex)
            {
                // Log specific error for this attachment processing to avoid losing context if one of many fails
                _logger.LogError(ex, "Error processing attachment {FileName} (ID: {FileId}) for inclusion in message content", fileAttachment.FileName, fileId);
                processedContent += $"\n[Error processing attachment: {fileAttachment.FileName}]";
            }
        }

        return processedContent;
    }
}