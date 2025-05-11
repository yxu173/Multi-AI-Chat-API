using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Domain.Repositories;
using Domain.Aggregates.Chats;
using Application.Features.Chats.GetChatById;
using Application.Services.Chat;
using Application.Services.Messaging;
using FastEndpoints;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly MessageService _messageService;

    private const int MAX_CLIENT_FILE_SIZE = 10 * 1024 * 1024;

    public ChatHub(
        ChatService chatService,
        IFileAttachmentRepository fileAttachmentRepository,
        MessageService messageService)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _fileAttachmentRepository = fileAttachmentRepository ??
                                    throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
    }


    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Console.WriteLine($"User {userId} connected to chat hub");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Console.WriteLine(
            $"User {userId} disconnected from chat hub. Reason: {exception?.Message ?? "Normal disconnection"}");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChatSession(string chatSessionId)
    {
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatSessionId);
            Console.WriteLine($"User {Context.UserIdentifier} joined chat session {chatSessionId}");

            try
            {
                var chatGuid = Guid.Parse(chatSessionId);
                var chatResult = await new GetChatByIdQuery(chatGuid).ExecuteAsync();

                if (chatResult.IsSuccess)
                {
                    await Clients.Caller.SendAsync("ReceiveChatHistory", chatResult.Value);
                }
            }
            catch (Exception historyEx)
            {
                Console.WriteLine($"Error loading chat history: {historyEx.Message}");
                await Clients.Caller.SendAsync("Error", $"Joined chat but failed to load history: {historyEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error joining chat session: {ex.Message}");
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
        string? safetyTolerance = null
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                await Clients.Caller.SendAsync("Error", "Message content cannot be empty");
                return;
            }

            var userId = Guid.Parse(Context.UserIdentifier);
            await _chatService.SendUserMessageAsync(
                Guid.Parse(chatSessionId),
                userId,
                content,
                enableThinking,
                imageSize,
                numImages,
                outputFormat,
                enableSafetyChecker,
                safetyTolerance,
                cancellationToken: default);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to send message: {ex.Message}");
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
        var userId = Guid.Parse(Context.UserIdentifier);

        try
        {
            string processedContent = content;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                processedContent = await ProcessFileAttachmentsAsync(processedContent, fileAttachmentIds);
            }

            await _chatService.SendUserMessageAsync(
                Guid.Parse(chatSessionId),
                userId,
                processedContent,
                enableThinking,
                imageSize,
                numImages,
                outputFormat,
                enableSafetyChecker,
                safetyTolerance,
                cancellationToken: default);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message with attachments: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Edit an existing message
    /// </summary>
    public async Task EditMessage(string chatSessionId, string messageId, string newContent)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newContent))
            {
                await Clients.Caller.SendAsync("Error", "Message content cannot be empty");
                return;
            }

            var userId = Guid.Parse(Context.UserIdentifier);
            await _chatService.EditUserMessageAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(messageId),
                newContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error editing message: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
        }
    }

    /// <summary>
    /// Edit a message with file attachments
    /// </summary>
    public async Task EditMessageWithAttachments(string chatSessionId, string messageId, string newContent,
        List<Guid> fileAttachmentIds)
    {
        var userId = Guid.Parse(Context.UserIdentifier);

        try
        {
            string processedContent = newContent;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                processedContent = await ProcessFileAttachmentsAsync(processedContent, fileAttachmentIds);
            }

            await _chatService.EditUserMessageAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(messageId),
                processedContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error editing message with attachments: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all file attachments for a message
    /// </summary>
    public async Task GetMessageAttachments(string messageId)
    {
        try
        {
            var attachments = await _messageService.GetMessageAttachmentsAsync(Guid.Parse(messageId));

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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving message attachments: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to get message attachments: {ex.Message}");
        }
    }

    /// <summary>
    /// Regenerates the AI response for a given user message.
    /// </summary>
    public async Task RegenerateResponse(string chatSessionId, string userMessageId)
    {
        try
        {
            var userId = Guid.Parse(Context.UserIdentifier);
            await _chatService.RegenerateAiResponseAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(userMessageId));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error regenerating response: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to regenerate response: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes file attachments and integrates them into the message content
    /// </summary>
    private async Task<string> ProcessFileAttachmentsAsync(string content, List<Guid> fileAttachmentIds)
    {
        var processedContent = content;

        foreach (var fileId in fileAttachmentIds)
        {
            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId);
            if (fileAttachment == null) continue;

            if (!File.Exists(fileAttachment.FilePath))
            {
                processedContent += $"\n[Attachment {fileAttachment.FileName} not found]";
                continue;
            }

            if (fileAttachment.FileSize > MAX_CLIENT_FILE_SIZE)
            {
                processedContent +=
                    $"\n[Attachment {fileAttachment.FileName} skipped: exceeds size limit of {MAX_CLIENT_FILE_SIZE / (1024 * 1024)}MB]";
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
                Console.WriteLine($"Error processing attachment {fileAttachment.FileName}: {ex.Message}");
                processedContent += $"\n[Error processing attachment: {fileAttachment.FileName}]";
            }
        }

        return processedContent;
    }
}