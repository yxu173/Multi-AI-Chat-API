using Microsoft.AspNetCore.SignalR;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Domain.Repositories;
using MediatR;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Domain.Aggregates.Chats;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Gif;
using Application.Features.Chats.GetChatById;

namespace Web.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly IMessageRepository _messageRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly MessageService _messageService;
    private readonly IMediator _mediator;

    // Constants for file processing
    private const int MAX_CLIENT_FILE_SIZE = 10 * 1024 * 1024; // 10MB
    private const int MAX_AI_FILE_SIZE = 5 * 1024 * 1024; // 5MB
    private const int SMALL_IMAGE_THRESHOLD = 10 * 1024; // 10KB

    public ChatHub(
        ChatService chatService,
        StreamingOperationManager streamingOperationManager,
        IMessageRepository messageRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        MessageService messageService,
        IMediator mediator)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _streamingOperationManager = streamingOperationManager ?? throw new ArgumentNullException(nameof(streamingOperationManager));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// Handles connection of a client to the hub
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Console.WriteLine($"User {userId} connected to chat hub");
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Handles disconnection of a client from the hub
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Console.WriteLine($"User {userId} disconnected from chat hub. Reason: {exception?.Message ?? "Normal disconnection"}");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Adds a client to a specific chat session group
    /// </summary>
    public async Task JoinChatSession(string chatSessionId)
    {
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatSessionId);
            Console.WriteLine($"User {Context.UserIdentifier} joined chat session {chatSessionId}");
            
            // Get chat history and send it to the client
            try
            {
                var chatGuid = Guid.Parse(chatSessionId);
                var query = new GetChatByIdQuery(chatGuid);
                var chatResult = await _mediator.Send(query);
                
                if (chatResult.IsSuccess)
                {
                    // Send chat history to the caller
                    await Clients.Caller.SendAsync("ReceiveChatHistory", chatResult.Value);
                }
            }
            catch (Exception historyEx)
            {
                Console.WriteLine($"Error loading chat history: {historyEx.Message}");
                // Don't throw - we still want the user to join the chat even if history fails
                await Clients.Caller.SendAsync("Error", $"Joined chat but failed to load history: {historyEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error joining chat session: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to join chat session: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a text message to the AI
    /// </summary>
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

    #region Private Helper Methods

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

            // Check file exists
            if (!File.Exists(fileAttachment.FilePath))
            {
                processedContent += $"\n[Attachment {fileAttachment.FileName} not found]";
                continue;
            }

            if (fileAttachment.FileSize > MAX_CLIENT_FILE_SIZE)
            {
                processedContent += $"\n[Attachment {fileAttachment.FileName} skipped: exceeds size limit of {MAX_CLIENT_FILE_SIZE / (1024 * 1024)}MB]";
                continue;
            }

            try
            {
                var fileBytes = await File.ReadAllBytesAsync(fileAttachment.FilePath);
                
                if (fileBytes.Length > MAX_AI_FILE_SIZE)
                {
                    processedContent += $"\n[Attachment {fileAttachment.FileName} skipped: too large for AI processing (max {MAX_AI_FILE_SIZE / (1024 * 1024)}MB)]";
                    continue;
                }
                
                if (fileAttachment.FileType == FileType.Image)
                {
                    processedContent = await ProcessImageAttachmentAsync(processedContent, fileAttachment, fileBytes);
                }
                else
                {
                    processedContent = ProcessDocumentAttachmentAsync(processedContent, fileAttachment, fileBytes);
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error reading file {fileAttachment.FilePath}: {ex.Message}");
                processedContent += $"\n[Error reading attachment: {fileAttachment.FileName}]";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing attachment {fileAttachment.FileName}: {ex.Message}");
                processedContent += $"\n[Error processing attachment: {fileAttachment.FileName}]";
            }
        }

        return processedContent;
    }

    /// <summary>
    /// Process an image attachment
    /// </summary>
    private async Task<string> ProcessImageAttachmentAsync(string content, FileAttachment fileAttachment, byte[] fileBytes)
    {
        string normalizedContentType = NormalizeImageContentType(fileAttachment.ContentType);

        if (string.IsNullOrEmpty(normalizedContentType))
        {
            normalizedContentType = DetectImageFormat(fileBytes);
        }

        if (string.IsNullOrEmpty(normalizedContentType))
        {
            return content + $"\n[Image attachment: {fileAttachment.FileName} (unsupported format)]";
        }

        byte[] optimizedImageBytes = OptimizeImageForAI(fileBytes, normalizedContentType);
        var base64Content = Convert.ToBase64String(optimizedImageBytes, Base64FormattingOptions.None);

        string imageTag = $"<image-base64:{normalizedContentType};base64,{base64Content}>";
        return content + $"\n\n{imageTag}\n\n";
    }

    /// <summary>
    /// Process a document attachment
    /// </summary>
    private string ProcessDocumentAttachmentAsync(string content, FileAttachment fileAttachment, byte[] fileBytes)
    {
        // Process document files for OpenAI
        if (IsCompatibleFileType(fileAttachment.ContentType) && fileBytes.Length <= MAX_AI_FILE_SIZE)
        {
            var base64Content = Convert.ToBase64String(fileBytes, Base64FormattingOptions.None);
            string fileTag = $"<file-base64:{fileAttachment.FileName}:{fileAttachment.ContentType};base64,{base64Content}>";
            return content + $"\n\n{fileTag}\n\n";
        }
        else
        {
            return content + $"\n[Document attachment: {fileAttachment.FileName} (not sent to AI due to incompatible format or size)]";
        }
    }

    private string DetectImageFormat(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 4)
            return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        if (bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return "image/gif";

        if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";

        return string.Empty;
    }

    private string NormalizeImageContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        string typeLower = contentType.ToLowerInvariant().Trim();

        if (typeLower == "image/jpg" || typeLower.StartsWith("image/jpeg"))
            return "image/jpeg";
        if (typeLower.StartsWith("image/png"))
            return "image/png";
        if (typeLower.StartsWith("image/gif"))
            return "image/gif";
        if (typeLower.StartsWith("image/webp"))
            return "image/webp";

        return string.Empty;
    }

    private byte[] OptimizeImageForAI(byte[] imageBytes, string contentType)
    {
        try
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return imageBytes;

            if (imageBytes.Length <= SMALL_IMAGE_THRESHOLD) // Skip optimization for very small images
                return imageBytes;

            using var imageStream = new MemoryStream(imageBytes);
            using var image = Image.Load(imageStream);

            int maxDimension = 384;
            int width = image.Width;
            int height = image.Height;

            double scaleFactor = 1.0;
            if (width > height && width > maxDimension)
            {
                scaleFactor = (double)maxDimension / width;
            }
            else if (height > width && height > maxDimension)
            {
                scaleFactor = (double)maxDimension / height;
            }

            if (scaleFactor >= 1.0 && imageBytes.Length <= 100 * 1024)
                return imageBytes;

            int newWidth = (int)(width * scaleFactor);
            int newHeight = (int)(height * scaleFactor);

            newWidth = Math.Max(newWidth, 32);
            newHeight = Math.Max(newHeight, 32);

            image.Mutate(x => x.Resize(newWidth, newHeight));

            using var resultStream = new MemoryStream();

            if (contentType == "image/jpeg")
            {
                int adaptiveQuality = DetermineOptimalQuality(imageBytes.Length);
                var encoder = new JpegEncoder { Quality = adaptiveQuality };
                image.Save(resultStream, encoder);
            }
            else if (contentType == "image/png")
            {
                var encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                image.Save(resultStream, encoder);
            }
            else if (contentType == "image/gif")
            {
                var encoder = new GifEncoder();
                image.Save(resultStream, encoder);
            }
            else if (contentType == "image/webp")
            {
                var encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                image.Save(resultStream, encoder); // Fallback to PNG as WebP isn't directly supported here
            }
            else
            {
                int adaptiveQuality = DetermineOptimalQuality(imageBytes.Length);
                var encoder = new JpegEncoder { Quality = adaptiveQuality };
                image.Save(resultStream, encoder);
            }

            var result = resultStream.ToArray();

            if (result.Length >= imageBytes.Length || (result.Length > 100 * 1024 && imageBytes.Length > 200 * 1024))
            {
                if (result.Length > 100 * 1024)
                {
                    Console.WriteLine($"First pass result: {result.Length / 1024}KB - still too large, trying second pass");
                    using var secondPassStream = new MemoryStream(result);
                    using var secondPassImage = Image.Load(secondPassStream);

                    int secondPassWidth = Math.Min(320, secondPassImage.Width);
                    secondPassImage.Mutate(x => x.Resize(secondPassWidth, 0));

                    using var secondResultStream = new MemoryStream();
                    var aggressiveEncoder = new JpegEncoder { Quality = 40 };
                    secondPassImage.Save(secondResultStream, aggressiveEncoder);

                    byte[] secondPassResult = secondResultStream.ToArray();
                    if (secondPassResult.Length < result.Length)
                    {
                        Console.WriteLine($"Second pass optimization: {result.Length / 1024}KB → {secondPassResult.Length / 1024}KB");
                        return secondPassResult;
                    }
                }

                return result.Length < imageBytes.Length ? result : imageBytes;
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error optimizing image: {ex.Message}");
            return imageBytes;
        }
    }

    private int DetermineOptimalQuality(int originalSizeBytes)
    {
        if (originalSizeBytes > 1024 * 1024) return 40;
        else if (originalSizeBytes > 500 * 1024) return 50;
        else if (originalSizeBytes > 200 * 1024) return 60;
        else return 70;
    }

    // Check if the file type is compatible with the AI service
    private bool IsCompatibleFileType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        
        string typeLower = contentType.ToLowerInvariant().Trim();
        
        // Common document types that are likely to work with AI services
        return typeLower.Contains("pdf") ||
               typeLower.Contains("text/") ||
               typeLower.Contains("application/json") ||
               typeLower.Contains("application/xml") ||
               typeLower.Contains("application/csv") ||
               typeLower.Contains("application/msword") ||
               typeLower.Contains("application/vnd.openxmlformats-officedocument") ||
               typeLower.Contains("application/vnd.ms-");
    }

    #endregion
}