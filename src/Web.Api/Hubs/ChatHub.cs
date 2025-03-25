using Microsoft.AspNetCore.SignalR;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Application.Notifications;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly IMessageRepository _messageRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IMediator _mediator;
    private readonly MessageService _messageService;

    public ChatHub(
        ChatService chatService, 
        StreamingOperationManager streamingOperationManager,
        IMessageRepository messageRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IMediator mediator,
        MessageService messageService)
    {
        _chatService = chatService;
        _streamingOperationManager = streamingOperationManager;
        _messageRepository = messageRepository;
        _fileAttachmentRepository = fileAttachmentRepository;
        _mediator = mediator;
        _messageService = messageService;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChatSession(string chatSessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, chatSessionId);
    }

    public async Task SendMessage(string chatSessionId, string content)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        
        // Check if this is a temporary message for file uploads
        if (content == "Uploading files...")
        {
            // For file upload temporary messages, use a special flag
            await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, content, isFileUpload: true);
        }
        else
        {
            // Normal message processing
            await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, content);
        }
    }

    public async Task EditMessage(string chatSessionId, string messageId, string newContent)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        await _chatService.EditUserMessageAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(messageId), newContent);
    }

    public async Task NotifyFileUploaded(string chatSessionId, string messageId, string fileId)
    {
        if (!Guid.TryParse(fileId, out var fileGuid) || 
            !Guid.TryParse(messageId, out var messageGuid) || 
            !Guid.TryParse(chatSessionId, out var chatSessionGuid))
        {
            return;
        }

        var userId = Guid.Parse(Context.UserIdentifier);
        
        // Get the file attachment
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileGuid, CancellationToken.None);
        if (fileAttachment == null)
        {
            return;
        }
        
        // Get the message
        var message = await _messageRepository.GetByIdAsync(messageGuid, CancellationToken.None);
        if (message == null || message.UserId != userId)
        {
            return;
        }
        
        // Ensure the file contains properly formatted base64 content if it's an image or PDF
        if ((fileAttachment.FileType == Domain.Aggregates.Chats.FileType.Image || 
             fileAttachment.FileType == Domain.Aggregates.Chats.FileType.PDF) && 
            !string.IsNullOrEmpty(fileAttachment.Base64Content))
        {
            // Make sure the base64 content is valid
            try
            {
                // Try to decode a small part to validate it's proper base64
                var sample = fileAttachment.Base64Content.Substring(0, Math.Min(100, fileAttachment.Base64Content.Length));
                Convert.FromBase64String(sample);
            }
            catch
            {
                // If there's an issue with the base64 content, log it (in a real app)
                Console.WriteLine($"Invalid base64 content for file {fileAttachment.Id}");
            }
        }
        
        // Notify other clients in the chat group
        await _mediator.Publish(new FileUploadedNotification(chatSessionGuid, fileAttachment));
    }

    public async Task SendMessageWithAttachments(string chatSessionId, string content, IEnumerable<string> fileIds)
    {
        var userId = Guid.Parse(Context.UserIdentifier);
        Console.WriteLine($"Received SendMessageWithAttachments request: chatId={chatSessionId}, userId={userId}, fileCount={fileIds?.Count() ?? 0}");
        
        try
        {
            // Get the session data for temporary files
            string tempFilesKey = $"TempFiles_{userId}";
            var tempFilesJson = Context.GetHttpContext().Session.GetString(tempFilesKey);
            var fileAttachments = new List<FileAttachment>();
            
            if (!string.IsNullOrEmpty(tempFilesJson) && fileIds != null && fileIds.Any())
            {
                var tempFiles = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(tempFilesJson);
                
                foreach (var fileIdStr in fileIds)
                {
                    if (Guid.TryParse(fileIdStr, out Guid fileId) && tempFiles.ContainsKey(fileIdStr))
                    {
                        var tempFileObj = tempFiles[fileIdStr];
                        var tempFileProps = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                            System.Text.Json.JsonSerializer.Serialize(tempFileObj));
                        
                        try
                        {
                            // Extract properties from temp file data
                            var fileName = tempFileProps["FileName"].ToString();
                            var filePath = tempFileProps["FilePath"].ToString();
                            var contentType = tempFileProps["ContentType"].ToString();
                            var fileSize = Convert.ToInt64(tempFileProps["FileSize"]);
                            var base64Content = tempFileProps.ContainsKey("Base64Content") ? tempFileProps["Base64Content"]?.ToString() : null;
                            
                            // Create file attachment
                            var fileAttachment = FileAttachment.CreateWithBase64(
                                Guid.Empty, // MessageId will be set when the message is created
                                fileName,
                                filePath,
                                contentType,
                                fileSize,
                                base64Content);
                            
                            fileAttachments.Add(fileAttachment);
                            Console.WriteLine($"Created file attachment for {fileName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error creating file attachment for {fileId}: {ex.Message}");
                        }
                    }
                }
            }
            
            // Send message with attachments
            await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, content, false, fileAttachments);
            
            // Clear the temporary files after sending
            if (!string.IsNullOrEmpty(tempFilesKey))
            {
                Context.GetHttpContext().Session.Remove(tempFilesKey);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SendMessageWithAttachments: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            throw;
        }
    }
}