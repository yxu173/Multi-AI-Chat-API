using System.Security.Claims;
using Application.Abstractions.Interfaces;
using Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Controllers;

[Authorize]
public class FilesController : BaseController
{
    private readonly IFileService _fileService;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IWebHostEnvironment _environment;

    public FilesController(
        IFileService fileService,
        IFileAttachmentRepository fileAttachmentRepository,
        IWebHostEnvironment environment)
    {
        _fileService = fileService;
        _fileAttachmentRepository = fileAttachmentRepository;
        _environment = environment;
    }

    [HttpPost("Upload/{chatSessionId}")]
    public async Task<IActionResult> UploadFile(
        [FromRoute] Guid chatSessionId,
        [FromForm] IFormFile file,
        [FromForm] string messageId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            Guid? messageGuid = null;
            
            // Try to parse messageId if it's provided
            if (!string.IsNullOrEmpty(messageId) && Guid.TryParse(messageId, out Guid parsedMessageId))
            {
                messageGuid = parsedMessageId;
            }
            
            // Log the details for debugging
            Console.WriteLine($"Uploading file: {file.FileName} for session {chatSessionId}, user {userId}, message {messageGuid}");
            
            var fileAttachment = await _fileService.UploadFileAsync(chatSessionId, file, userId, cancellationToken);

            // If we have a valid message ID, link this attachment to the message
            if (messageGuid.HasValue)
            {
                fileAttachment.LinkToMessage(messageGuid.Value);
                await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);
            }

            return Ok(new
            {
                Id = fileAttachment.Id,
                FileName = fileAttachment.FileName,
                ContentType = fileAttachment.ContentType,
                FileType = fileAttachment.FileType.ToString(),
                Size = fileAttachment.FileSize,
                HasBase64 = fileAttachment.Base64Content != null
            });
        }
        catch (Exception ex)
        {
            // Log the full exception details
            Console.WriteLine($"File upload failed: {ex.Message}");
            Console.WriteLine($"Exception details: {ex}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            
            return StatusCode(500, new { Error = $"File upload failed: {ex.Message}" });
        }
    }
}