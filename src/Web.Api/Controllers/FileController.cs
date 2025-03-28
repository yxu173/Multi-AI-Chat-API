using Application.Services;
using Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileController : BaseController
{
    private readonly FileUploadService _fileUploadService;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;

    public FileController(
        FileUploadService fileUploadService,
        IFileAttachmentRepository fileAttachmentRepository)
    {
        _fileUploadService = fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)] 
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromQuery] Guid? messageId = null)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file was uploaded");
            }

            // Validate file type and size
            if (!IsAllowedFileType(file.ContentType))
            {
                return BadRequest("File type not allowed");
            }

            if (file.Length > 30_000_000) 
            {
                return BadRequest("File size exceeds the limit (30MB)");
            }

            var fileAttachment = await _fileUploadService.UploadFileAsync(file, messageId);

            return Ok(new
            {
                id = fileAttachment.Id,
                fileName = fileAttachment.FileName,
                contentType = fileAttachment.ContentType,
                fileType = fileAttachment.FileType.ToString(),
                fileSize = fileAttachment.FileSize,
                messageId = fileAttachment.MessageId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFile(Guid id, [FromQuery] string token = null)
    {
        try
        {
            if (string.IsNullOrEmpty(token) && !User.Identity.IsAuthenticated)
            {
                return Unauthorized("Authentication required");
            }

            if (!string.IsNullOrEmpty(token) && !User.Identity.IsAuthenticated)
            {
               
            }

            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(id);
            if (fileAttachment == null)
            {
                return NotFound();
            }

            if (!System.IO.File.Exists(fileAttachment.FilePath))
            {
                return NotFound("File not found on server");
            }

            var fileStream = new FileStream(fileAttachment.FilePath, FileMode.Open, FileAccess.Read);
            return File(fileStream, fileAttachment.ContentType, fileAttachment.FileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        try
        {
            await _fileUploadService.DeleteFileAsync(id);
            return Ok();
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("message/{messageId}")]
    public async Task<IActionResult> GetFilesByMessageId(Guid messageId)
    {
        try
        {
            // Get the base URL from the request
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            
            var files = await _fileAttachmentRepository.GetByMessageIdAsync(messageId);
            var result = new List<object>();

            foreach (var file in files)
            {
                result.Add(new
                {
                    id = file.Id,
                    fileName = file.FileName,
                    contentType = file.ContentType,
                    fileType = file.FileType.ToString(),
                    fileSize = file.FileSize,
                    url = $"{baseUrl}/api/file/{file.Id}"
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    private bool IsAllowedFileType(string contentType)
    {
        // Allow images, documents, PDFs
        return contentType.StartsWith("image/") ||
               contentType.StartsWith("application/pdf") ||
               contentType.StartsWith("application/msword") ||
               contentType.StartsWith("application/vnd.openxmlformats-officedocument") ||
               contentType.StartsWith("text/");
    }
} 