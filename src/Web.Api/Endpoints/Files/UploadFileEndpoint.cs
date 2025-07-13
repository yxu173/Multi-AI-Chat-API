using Application.Services;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Application.Services.Files;

namespace Web.Api.Endpoints.Files;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;
    public Guid? MessageId { get; set; }
}

public class UploadFileResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public Guid? MessageId { get; set; }
}

[Authorize]
public class UploadFileEndpoint : Endpoint<UploadFileRequest, UploadFileResponse>
{
    private readonly FileUploadService _fileUploadService;

    public UploadFileEndpoint(
        FileUploadService fileUploadService)
    {
        _fileUploadService = fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
    }

    public override void Configure()
    {
        Post("/api/file/upload");
        AllowFileUploads();
        Description(x => x
            .Produces(200, typeof(UploadFileResponse))
            .Produces(400)
            .Produces(500));
    }

    public override async Task HandleAsync(UploadFileRequest req, CancellationToken ct)
    {
        try
        {
            if (req.File == null || req.File.Length == 0)
            {
                AddError("No file was uploaded");
                await SendErrorsAsync(400, ct);
                return;
            }

            if (!IsAllowedFileType(req.File.ContentType))
            {
                AddError($"File type '{req.File.ContentType}' is not allowed. Only PDF, CSV, plain text, and image files (JPEG, PNG, GIF, WebP) are supported.");
                await SendErrorsAsync(400, ct);
                return;
            }

            if (req.File.Length > 10_000_000)
            {
                AddError("File size exceeds the limit (30MB)");
                await SendErrorsAsync(400, ct);
                return;
            }

            var fileAttachment = await _fileUploadService.UploadFileAsync(req.File, req.MessageId, ct);

            await SendAsync(new UploadFileResponse
            {
                Id = fileAttachment.Id,
                FileName = fileAttachment.FileName,
                ContentType = fileAttachment.ContentType,
                FileType = fileAttachment.FileType.ToString(),
                FileSize = fileAttachment.FileSize,
                MessageId = fileAttachment.MessageId
            }, cancellation: ct);
        }
        catch (Exception ex)
        {
            AddError(ex.Message);
            await SendErrorsAsync(500, ct);
        }
    }

    private bool IsAllowedFileType(string contentType)
    {
        return contentType == "application/pdf" ||
               contentType == "text/csv" ||
               contentType == "application/csv" ||
               contentType == "text/plain" ||
               contentType == "image/jpeg" ||
               contentType == "image/png" ||
               contentType == "image/gif" ||
               contentType == "image/webp";
    }
} 