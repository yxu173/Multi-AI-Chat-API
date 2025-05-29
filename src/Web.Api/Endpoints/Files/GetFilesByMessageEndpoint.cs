using Domain.Repositories;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Web.Api.Endpoints.Files;

public class GetFilesByMessageRequest
{
    public Guid MessageId { get; set; }
}

public class FileDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Url { get; set; } = string.Empty;
}

[Authorize]
public class GetFilesByMessageEndpoint : Endpoint<GetFilesByMessageRequest, List<FileDto>>
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetFilesByMessageEndpoint(
        IFileAttachmentRepository fileAttachmentRepository,
        IHttpContextAccessor httpContextAccessor)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public override void Configure()
    {
        Get("/api/file/message/{MessageId}");
    }

    public override async Task HandleAsync(GetFilesByMessageRequest req, CancellationToken ct)
    {
        try
        {
            // Get the base URL from the request
            var request = _httpContextAccessor.HttpContext!.Request;
            var baseUrl = $"{request.Scheme}://{request.Host}";
            
            var files = await _fileAttachmentRepository.GetByMessageIdAsync(req.MessageId, ct);
            var result = new List<FileDto>();

            foreach (var file in files)
            {
                result.Add(new FileDto
                {
                    Id = file.Id,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    FileType = file.FileType.ToString(),
                    FileSize = file.FileSize,
                    Url = $"{baseUrl}/api/file/{file.Id}"
                });
            }

            await SendAsync(result, cancellation: ct);
        }
        catch (Exception ex)
        {
            AddError($"Internal server error: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }
} 