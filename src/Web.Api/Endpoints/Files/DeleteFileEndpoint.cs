using Application.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;

namespace Web.Api.Endpoints.Files;

public class DeleteFileRequest
{
    public Guid Id { get; set; }
}

[Authorize]
public class DeleteFileEndpoint : Endpoint<DeleteFileRequest>
{
    private readonly FileUploadService _fileUploadService;

    public DeleteFileEndpoint(FileUploadService fileUploadService)
    {
        _fileUploadService = fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
    }

    public override void Configure()
    {
        Delete("/api/file/{Id}");
    }

    public override async Task HandleAsync(DeleteFileRequest req, CancellationToken ct)
    {
        try
        {
            await _fileUploadService.DeleteFileAsync(req.Id, ct);
            await SendOkAsync(ct);
        }
        catch (Exception ex)
        {
            AddError($"Internal server error: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }
} 