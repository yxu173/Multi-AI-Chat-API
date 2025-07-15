using Domain.Repositories;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using System.IO;

namespace Web.Api.Endpoints.Files;

public class GetFileRequest
{
    public Guid Id { get; set; }
    public string? Token { get; set; }
}

[AllowAnonymous]
public class GetFileEndpoint : Endpoint<GetFileRequest>
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;

    public GetFileEndpoint(IFileAttachmentRepository fileAttachmentRepository)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
    }

    public override void Configure()
    {
        Get("/api/file/{Id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetFileRequest req, CancellationToken ct)
    {
        try
        {
            // if (string.IsNullOrEmpty(req.Token) && !User.Identity!.IsAuthenticated)
            // {
            //     await SendUnauthorizedAsync(ct);
            //     return;
            // }

            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(req.Id, ct);
            if (fileAttachment == null)
            {
                await SendNotFoundAsync(ct);
                return;
            }

            if (!File.Exists(fileAttachment.FilePath))
            {
                AddError("File not found on server");
                await SendErrorsAsync(404, ct);
                return;
            }

            // Read the file and send as bytes directly
            var fileBytes = await File.ReadAllBytesAsync(fileAttachment.FilePath, ct);
            await SendBytesAsync(
                bytes: fileBytes,
                fileName: fileAttachment.FileName,
                contentType: fileAttachment.ContentType,
                cancellation: ct);
        }
        catch (Exception ex)
        {
            AddError($"Internal server error: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }
} 