using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Services.UploadFile;

public class FileService : IFileService
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IWebHostEnvironment _environment;

    public FileService(
        IFileAttachmentRepository fileAttachmentRepository,
        IWebHostEnvironment environment)
    {
        _fileAttachmentRepository = fileAttachmentRepository;
        _environment = environment;
    }

    public async Task<FileAttachment> UploadFileAsync(
        Guid chatSessionId,
        IFormFile file,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (file.Length > 10 * 1024 * 1024)
            throw new ArgumentException("File size exceeds limit (10MB)");

        var uploadDirectory = Path.Combine(_environment.ContentRootPath, "uploads", chatSessionId.ToString());
        if (!Directory.Exists(uploadDirectory))
            Directory.CreateDirectory(uploadDirectory);

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var filePath = Path.Combine(uploadDirectory, uniqueFileName);
        var relativePath = uniqueFileName;

        string base64Content = null;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            if (file.ContentType.StartsWith("image/") || file.ContentType == "application/pdf")
            {
                byte[] fileBytes = memoryStream.ToArray();
                if (fileBytes.Length > 2 * 1024 * 1024)
                {
                    if (fileBytes.Length > 5 * 1024 * 1024)
                    {
                        fileBytes = fileBytes.Take(5 * 1024 * 1024).ToArray();
                    }
                }

                base64Content = Convert.ToBase64String(fileBytes);
            }

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(fileStream, cancellationToken);
            }
        }

        var fileAttachment = FileAttachment.CreateWithBase64(
            file.FileName,
            relativePath,
            file.ContentType,
            file.Length,
            base64Content,
            null
        );

        await _fileAttachmentRepository.AddAsync(fileAttachment, cancellationToken);

        return fileAttachment;
    }
}