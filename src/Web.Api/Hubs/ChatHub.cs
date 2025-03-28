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
    private readonly FileUploadService _fileUploadService;

    public ChatHub(
        ChatService chatService,
        StreamingOperationManager streamingOperationManager,
        IMessageRepository messageRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IMediator mediator,
        MessageService messageService,
        FileUploadService fileUploadService)
    {
        _chatService = chatService;
        _streamingOperationManager = streamingOperationManager;
        _messageRepository = messageRepository;
        _fileAttachmentRepository = fileAttachmentRepository;
        _mediator = mediator;
        _messageService = messageService;
        _fileUploadService = fileUploadService;
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
        try
        {
            var userId = Guid.Parse(Context.UserIdentifier);
            await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to send message: {ex.Message}");
            throw;
        }
    }

    public async Task SendMessageWithAttachments(string chatSessionId, string content, List<Guid> fileAttachmentIds)
    {
        var userId = Guid.Parse(Context.UserIdentifier);

        try
        {
            string processedContent = content;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                foreach (var fileId in fileAttachmentIds)
                {
                    var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId);
                    if (fileAttachment == null) continue;

                    if (fileAttachment.FileSize > 10 * 1024 * 1024)
                    {
                        processedContent += $"\n[Attachment {fileAttachment.FileName} skipped: exceeds size limit]";
                        continue;
                    }

                    if (fileAttachment.FileType == FileType.Image)
                    {
                        var fileBytes = await File.ReadAllBytesAsync(fileAttachment.FilePath);
                        string normalizedContentType = NormalizeImageContentType(fileAttachment.ContentType);

                        if (string.IsNullOrEmpty(normalizedContentType))
                        {
                            normalizedContentType = DetectImageFormat(fileBytes);
                        }

                        if (string.IsNullOrEmpty(normalizedContentType))
                        {
                            processedContent += $"\n[Image attachment: {fileAttachment.FileName} (unsupported format)]";
                            continue;
                        }

                        byte[] optimizedImageBytes = OptimizeImageForAI(fileBytes, normalizedContentType);
                        var base64Content = Convert.ToBase64String(optimizedImageBytes, Base64FormattingOptions.None);

                        string imageTag = $"<image-base64:{normalizedContentType};base64,{base64Content}>";
                        processedContent += $"\n\n{imageTag}\n\n";
                    }
                    else
                    {
                        processedContent += $"\n[Document attachment: {fileAttachment.FileName}]";
                    }
                }
            }

            await _chatService.SendUserMessageAsync(Guid.Parse(chatSessionId), userId, processedContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message with attachments: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to send message: {ex.Message}");
            throw;
        }
    }

    public async Task EditMessage(string chatSessionId, string messageId, string newContent)
    {
        try
        {
            var userId = Guid.Parse(Context.UserIdentifier);
            await _chatService.EditUserMessageAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(messageId),
                newContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error editing message: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
            throw;
        }
    }

    public async Task EditMessageWithAttachments(string chatSessionId, string messageId, string newContent,
        List<Guid> fileAttachmentIds)
    {
        var userId = Guid.Parse(Context.UserIdentifier);

        try
        {
            string processedContent = newContent;

            if (fileAttachmentIds != null && fileAttachmentIds.Any())
            {
                foreach (var fileId in fileAttachmentIds)
                {
                    var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId);
                    if (fileAttachment == null) continue;

                    if (fileAttachment.FileSize > 10 * 1024 * 1024) // 10 MB to match configured limit
                    {
                        processedContent += $"\n[Attachment {fileAttachment.FileName} skipped: exceeds size limit]";
                        continue;
                    }

                    if (fileAttachment.FileType == FileType.Image)
                    {
                        var fileBytes = await File.ReadAllBytesAsync(fileAttachment.FilePath);
                        string normalizedContentType = NormalizeImageContentType(fileAttachment.ContentType);

                        if (string.IsNullOrEmpty(normalizedContentType))
                        {
                            normalizedContentType = DetectImageFormat(fileBytes);
                        }

                        if (string.IsNullOrEmpty(normalizedContentType))
                        {
                            processedContent += $"\n[Image attachment: {fileAttachment.FileName} (unsupported format)]";
                            continue;
                        }

                        byte[] optimizedImageBytes = OptimizeImageForAI(fileBytes, normalizedContentType);
                        var base64Content = Convert.ToBase64String(optimizedImageBytes, Base64FormattingOptions.None);

                        string imageTag = $"<image-base64:{normalizedContentType};base64,{base64Content}>";
                        processedContent += $"\n\n{imageTag}\n\n";
                    }
                    else
                    {
                        processedContent += $"\n[Document attachment: {fileAttachment.FileName}]";
                    }
                }
            }

            await _chatService.EditUserMessageAsync(Guid.Parse(chatSessionId), userId, Guid.Parse(messageId),
                processedContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error editing message with attachments: {ex.Message}");
            await Clients.Caller.SendAsync("Error", $"Failed to edit message: {ex.Message}");
            throw;
        }
    }

    public async Task GetMessageAttachments(string messageId)
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

            if (imageBytes.Length <= 10 * 1024) // Skip optimization for very small images
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
                    Console.WriteLine(
                        $"First pass result: {result.Length / 1024}KB - still too large, trying second pass");
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
                        Console.WriteLine(
                            $"Second pass optimization: {result.Length / 1024}KB → {secondPassResult.Length / 1024}KB");
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
}