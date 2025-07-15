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
using Application.Features.Chats.GetAllChatsByUserId;

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
        using var activity = ActivitySource.StartActivity();
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation("User {UserId} connected to chat hub with ConnectionId {ConnectionId}", userId,
            Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        using var activity = ActivitySource.StartActivity();
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (exception != null)
        {
            _logger.LogInformation(exception,
                "User {UserId} disconnected from chat hub with ConnectionId {ConnectionId}. Reason: {Reason}", userId,
                Context.ConnectionId, exception.Message);
        }
        else
        {
            _logger.LogInformation(
                "User {UserId} disconnected from chat hub with ConnectionId {ConnectionId} (Normal disconnection)",
                userId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChatSession(string chatSessionId)
    {
        using var activity = ActivitySource.StartActivity();

        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                _logger.LogWarning("JoinChatSession called with empty chat session ID");
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                _logger.LogWarning("JoinChatSession called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, chatSessionId);
            _logger.LogInformation("User {UserId} joined chat session {ChatSessionId}", Context.UserIdentifier,
                chatSessionId);

            try
            {
                var chatResult = await new GetChatByIdQuery(chatGuid).ExecuteAsync();

                if (chatResult.IsSuccess)
                {
                    await Clients.Caller.SendAsync("ReceiveChatHistory", chatResult.Value);
                    activity?.AddEvent(new ActivityEvent("Chat history sent to caller."));
                }
                else
                {
                    _logger.LogWarning("Failed to fetch chat history for session {ChatSessionId}. Error: {Error}",
                        chatSessionId, chatResult.Error?.Description);
                    await Clients.Caller.SendAsync("Error",
                        $"Joined chat but failed to load history: {chatResult.Error?.Description}");
                }
            }
            catch (Exception historyEx)
            {
                _logger.LogError(historyEx, "Error loading chat history for session {ChatSessionId}", chatSessionId);
                await Clients.Caller.SendAsync("Error", $"Joined chat but failed to load history: {historyEx.Message}");
            }
        }
        catch (Exception ex)
        {
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

            var command = new SendMessageCommand(
                chatSessionGuid,
                userId,
                content,
                enableThinking,
                imageSize,
                numImages,
                outputFormat,
                enableSafetyChecker,
                safetyTolerance,
                enableDeepSearch
            );

            await command.ExecuteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message in chat {ChatSessionId}", chatSessionId);
            await Clients.Caller.SendAsync("Error", "Failed to send message");
        }
    }

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
        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                _logger.LogWarning("SendMessageWithAttachments called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            if (Context.UserIdentifier == null)
            {
                _logger.LogWarning("Method called with null user identifier");
                await Clients.Caller.SendAsync("Error", "User not authenticated");
                return;
            }

            if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
            {
                _logger.LogWarning("Method called with invalid user identifier");
                await Clients.Caller.SendAsync("Error", "Invalid user identifier");
                return;
            }

            string processedContent = content ?? string.Empty;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                using var processAttachmentsActivity = ActivitySource.StartActivity("ProcessAttachmentsInHub");
                processedContent = await ProcessFileAttachmentsAsync(processedContent, fileAttachmentIds);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message with attachments in session {ChatSessionId}", chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to send message: {ex.Message}");
        }
    }

    public async Task EditMessage(string chatSessionId, string messageId, string newContent)
    {
        using var activity = ActivitySource.StartActivity();

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
                _logger.LogWarning(
                    "EditMessage called with empty new content for message {MessageId} in session {ChatSessionId}",
                    messageId, chatSessionId);
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
            _logger.LogError(ex, "Error editing message {MessageId} in session {ChatSessionId}", messageId,
                chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
        }
    }

    public async Task EditMessageWithAttachments(string chatSessionId, string messageId, string newContent,
        List<Guid> fileAttachmentIds)
    {
        using var activity = ActivitySource.StartActivity();

        try
        {
            if (string.IsNullOrWhiteSpace(chatSessionId))
            {
                _logger.LogWarning("EditMessageWithAttachments called with empty chat session ID");
                await Clients.Caller.SendAsync("Error", "Chat session ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(chatSessionId, out Guid chatGuid))
            {
                _logger.LogWarning("EditMessageWithAttachments called with invalid chat session ID format");
                await Clients.Caller.SendAsync("Error", "Invalid chat session ID format");
                return;
            }

            if (string.IsNullOrWhiteSpace(messageId))
            {
                _logger.LogWarning("EditMessageWithAttachments called with empty message ID");
                await Clients.Caller.SendAsync("Error", "Message ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(messageId, out Guid messageGuid))
            {
                _logger.LogWarning("EditMessageWithAttachments called with invalid message ID format");
                await Clients.Caller.SendAsync("Error", "Invalid message ID format");
                return;
            }

            if (Context.UserIdentifier == null)
            {
                _logger.LogWarning("Method called with null user identifier");
                await Clients.Caller.SendAsync("Error", "User not authenticated");
                return;
            }

            if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
            {
                _logger.LogWarning("Method called with invalid user identifier");
                await Clients.Caller.SendAsync("Error", "Invalid user identifier");
                return;
            }

            string processedContent = newContent ?? string.Empty;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                processedContent = await ProcessFileAttachmentsAsync(processedContent, fileAttachmentIds);
            }

            var command = new EditMessageCommand(chatGuid, userId, messageGuid, processedContent);
            await command.ExecuteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing message {MessageId} with attachments in session {ChatSessionId}",
                messageId, chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
        }
    }

    public async Task GetMessageAttachments(string messageId)
    {
        using var activity = ActivitySource.StartActivity();

        try
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                _logger.LogWarning("GetMessageAttachments called with empty message ID");
                await Clients.Caller.SendAsync("Error", "Message ID cannot be empty");
                return;
            }

            if (!Guid.TryParse(messageId, out Guid messageGuid))
            {
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

            activity?.AddEvent(new ActivityEvent("Attachments sent to caller.",
                tags: new ActivityTagsCollection { { "count", attachments.Count } }));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error retrieving message attachments.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error retrieving message attachments for message {MessageId}", messageId);
            await Clients.Caller.SendAsync("Error", $"Failed to get message attachments: {ex.Message}");
        }
    }

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
            _logger.LogError(ex, "Error regenerating response for message {UserMessageId} in session {ChatSessionId}",
                userMessageId, chatSessionId);
            await Clients.Caller.SendAsync("Error", $"Failed to regenerate response: {ex.Message}");
        }
    }

    public async Task GetAllChats(int page = 1, int pageSize = 20)
    {
        using var activity = ActivitySource.StartActivity(nameof(GetAllChats));
        activity?.SetTag("user.id", Context.UserIdentifier);
        activity?.SetTag("request.page", page);
        activity?.SetTag("request.page_size", pageSize);

        try
        {
            if (Context.UserIdentifier == null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "User identifier is null.");
                _logger.LogWarning("GetAllChats called with null user identifier");
                await Clients.Caller.SendAsync("Error", "User not authenticated");
                return;
            }

            if (!Guid.TryParse(Context.UserIdentifier, out Guid userId))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid user identifier format.");
                _logger.LogWarning("GetAllChats called with invalid user identifier");
                await Clients.Caller.SendAsync("Error", "Invalid user identifier");
                return;
            }

            var query = new GetAllChatsByUserIdQuery(userId, page, pageSize);
            var result = await query.ExecuteAsync();

            if (result.IsSuccess)
            {
                activity?.SetTag("response.success", true);
                activity?.SetTag("response.folders_count", result.Value.Folders.Count);
                activity?.SetTag("response.root_chats_count", result.Value.RootChats.Count);
                activity?.SetTag("response.total_count", result.Value.RootChatsTotalCount);

                await Clients.Caller.SendAsync("ReceiveAllChats", result.Value);
                activity?.AddEvent(new ActivityEvent("All chats sent to caller."));
            }
            else
            {
                activity?.SetTag("response.success", false);
                activity?.SetTag("response.error", result.Error.Description ?? "Unknown error");
                _logger.LogWarning("Failed to get all chats for user {UserId}. Error: {Error}", userId,
                    result.Error?.Description);
                await Clients.Caller.SendAsync("Error", $"Failed to get chats: {result.Error?.Description}");
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Error getting all chats.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Error getting all chats for user {UserId}", Context.UserIdentifier);
            await Clients.Caller.SendAsync("Error", $"Failed to get chats: {ex.Message}");
        }
    }

    private async Task<string> ProcessFileAttachmentsAsync(string content, List<Guid> fileAttachmentIds)
    {
        var processedContent = content ?? string.Empty;

        foreach (var fileId in fileAttachmentIds)
        {
            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId);
            if (fileAttachment == null)
            {
                _logger.LogWarning(
                    "File attachment with ID {FileId} not found in repository during content processing.", fileId);
                processedContent += $"\n[Attachment metadata not found for ID: {fileId}]";
                continue;
            }

            if (string.IsNullOrWhiteSpace(fileAttachment.FilePath))
            {
                _logger.LogError(
                    "File attachment with ID {FileId} has an empty or null FilePath during content processing.",
                    fileId);
                processedContent += $"\n[Attachment {fileAttachment.FileName} has invalid path information]";
                continue;
            }

            if (!System.IO.File.Exists(fileAttachment.FilePath))
            {
                _logger.LogWarning(
                    "File attachment {FileName} (ID: {FileId}) not found at path: {FilePath} during content processing",
                    fileAttachment.FileName, fileId, fileAttachment.FilePath);
                processedContent += $"\n[Attachment {fileAttachment.FileName} not found on disk]";
                continue;
            }

            if (fileAttachment.FileSize > MaxClientFileSize)
            {
                _logger.LogWarning(
                    "File attachment {FileName} (ID: {FileId}, Size: {FileSize}) exceeds max client size {MaxFileSize} and was skipped during content processing.",
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
                _logger.LogError(ex,
                    "Error processing attachment {FileName} (ID: {FileId}) for inclusion in message content",
                    fileAttachment.FileName, fileId);
                processedContent += $"\n[Error processing attachment: {fileAttachment.FileName}]";
            }
        }

        return processedContent;
    }
}