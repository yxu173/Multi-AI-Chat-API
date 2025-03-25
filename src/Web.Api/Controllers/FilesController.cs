using System.Security.Claims;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Infrastructure;

namespace Web.Api.Controllers;

[Authorize]
public class FilesController : BaseController
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _environment;

    public FilesController(
        IFileAttachmentRepository fileAttachmentRepository,
        IMessageRepository messageRepository,
        IMediator mediator,
        IWebHostEnvironment environment)
    {
        _fileAttachmentRepository = fileAttachmentRepository;
        _messageRepository = messageRepository;
        _mediator = mediator;
        _environment = environment;
    }

    [HttpPost("Upload/{messageId}")]
    public async Task<IActionResult> UploadFile(
        [FromRoute] Guid messageId, 
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            
            var message = await _messageRepository.GetByIdAsync(messageId, cancellationToken);
            if (message == null)
                return NotFound(new { Error = "Message not found" });

            
            if (message.UserId.ToString() != User.FindFirstValue(ClaimTypes.NameIdentifier))
                return Forbid();

            
            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { Error = "File size exceeds limit (10MB)" });

            
            var uploadDirectory = Path.Combine(_environment.ContentRootPath, "uploads", message.ChatSessionId.ToString());
            if (!Directory.Exists(uploadDirectory))
                Directory.CreateDirectory(uploadDirectory);

            
            var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(uploadDirectory, uniqueFileName);

            // Convert file to Base64 for embedding in messages
            string base64Content = null;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;
                
                // Limit base64 storage to image and PDF files to avoid excessive database size
                if (file.ContentType.StartsWith("image/") || file.ContentType == "application/pdf")
                {
                    byte[] fileBytes = memoryStream.ToArray();
                    base64Content = Convert.ToBase64String(fileBytes);
                }
                
                // Save the file to disk
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    memoryStream.Position = 0;
                    await memoryStream.CopyToAsync(fileStream, cancellationToken);
                }
            }

           
            var fileAttachment = FileAttachment.CreateWithBase64(
                messageId,
                file.FileName,
                uniqueFileName,
                file.ContentType,
                file.Length,
                base64Content);

         
            await _fileAttachmentRepository.AddAsync(fileAttachment, cancellationToken);

          
            await _mediator.Publish(new FileUploadedNotification(message.ChatSessionId, fileAttachment), cancellationToken);

       
            return Ok(new
            {
                Id = fileAttachment.Id,
                FileName = fileAttachment.FileName,
                ContentType = fileAttachment.ContentType,
                FileType = fileAttachment.FileType.ToString(),
                Size = fileAttachment.FileSize
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = $"File upload failed: {ex.Message}" });
        }
    }

    [HttpGet("{fileId}")]
    public async Task<IActionResult> GetFile([FromRoute] Guid fileId, CancellationToken cancellationToken)
    {
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
        if (fileAttachment == null)
            return NotFound(new { Error = "File not found" });

        var message = await _messageRepository.GetByIdAsync(fileAttachment.MessageId, cancellationToken);
        if (message == null)
            return NotFound(new { Error = "Message not found" });

        if (message.UserId.ToString() != User.FindFirstValue(ClaimTypes.NameIdentifier))
            return Forbid();

        var filePath = Path.Combine(_environment.ContentRootPath, "uploads", message.ChatSessionId.ToString(), fileAttachment.FilePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { Error = "File not found on server" });

        return PhysicalFile(filePath, fileAttachment.ContentType, fileAttachment.FileName);
    }

    [HttpDelete("{fileId}")]
    public async Task<IActionResult> DeleteFile([FromRoute] Guid fileId, CancellationToken cancellationToken)
    {
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
        if (fileAttachment == null)
            return NotFound(new { Error = "File not found" });

        var message = await _messageRepository.GetByIdAsync(fileAttachment.MessageId, cancellationToken);
        if (message == null)
            return NotFound(new { Error = "Message not found" });

        if (message.UserId.ToString() != User.FindFirstValue(ClaimTypes.NameIdentifier))
            return Forbid();

       
        var filePath = Path.Combine(_environment.ContentRootPath, "uploads", message.ChatSessionId.ToString(), fileAttachment.FilePath);
        
       
        if (System.IO.File.Exists(filePath))
        {
            try
            {
                System.IO.File.Delete(filePath);
            }
            catch (Exception ex)
            {
                
                Console.WriteLine($"Error deleting file: {ex.Message}");
            }
        }

       
        await _fileAttachmentRepository.DeleteAsync(fileId, cancellationToken);

        return Ok(new { Message = "File deleted successfully" });
    }
}