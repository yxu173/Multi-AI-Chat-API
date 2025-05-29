using Domain.Repositories;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Web.Api.Endpoints.Files;

public class GetFileBase64Request
{
    public Guid Id { get; set; }
    public bool OptimizeForLlm { get; set; } = true;
}

public class FileBase64Response
{
    public string Base64Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
}

[Authorize]
public class GetFileBase64Endpoint : Endpoint<GetFileBase64Request, FileBase64Response>
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private const int MAX_LLM_FILE_SIZE = 5 * 1024 * 1024; // 5MB limit for LLMs

    public GetFileBase64Endpoint(IFileAttachmentRepository fileAttachmentRepository)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
    }

    public override void Configure()
    {
        Get("/api/file/{Id}/base64");
        Description(x => x
            .Produces(200, typeof(FileBase64Response))
            .Produces(404)
            .Produces(500));
    }

    public override async Task HandleAsync(GetFileBase64Request req, CancellationToken ct)
    {
        try
        {
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

            byte[] fileBytes;
            
            // Read file content
            fileBytes = await File.ReadAllBytesAsync(fileAttachment.FilePath, ct);
            
            // For images, optimize if requested and if image type
            if (req.OptimizeForLlm && fileAttachment.FileType == Domain.Aggregates.Chats.FileType.Image)
            {
                fileBytes = await OptimizeImageForLlmAsync(fileBytes, fileAttachment.ContentType, ct);
            }
            
            // Check if the file is too large for LLM
            if (fileBytes.Length > MAX_LLM_FILE_SIZE)
            {
                throw new InvalidOperationException($"File is too large for LLM processing. Maximum size is {MAX_LLM_FILE_SIZE / (1024 * 1024)}MB");
            }
            
            // Convert to base64
            string base64Content = Convert.ToBase64String(fileBytes);
            
            await SendAsync(new FileBase64Response
            {
                Base64Content = base64Content,
                ContentType = fileAttachment.ContentType,
                FileName = fileAttachment.FileName,
                FileType = fileAttachment.FileType.ToString()
            }, cancellation: ct);
        }
        catch (Exception ex)
        {
            AddError($"Error converting file to base64: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }

    private async Task<byte[]> OptimizeImageForLlmAsync(byte[] imageBytes, string contentType, CancellationToken ct)
    {
        try
        {
            using var imageStream = new MemoryStream(imageBytes);
            using var image = await Image.LoadAsync(imageStream, ct);
            
            // Resize to reasonable dimensions for LLMs
            int maxDimension = 800;
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
            
            if (scaleFactor < 1.0)
            {
                int newWidth = (int)(width * scaleFactor);
                int newHeight = (int)(height * scaleFactor);
                
                image.Mutate(x => x.Resize(newWidth, newHeight));
            }
            
            // Compress the image
            using var resultStream = new MemoryStream();
            var encoder = new JpegEncoder { Quality = 80 };
            await image.SaveAsJpegAsync(resultStream, encoder, ct);
            
            return resultStream.ToArray();
        }
        catch
        {
            // If optimization fails, return original bytes
            return imageBytes;
        }
    }
} 