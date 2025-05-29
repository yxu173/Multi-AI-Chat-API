using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.AI.RequestHandling.Models;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.RequestHandling;

public class FileAttachmentService : IFileAttachmentService
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly ILogger<FileAttachmentService> _logger;

    public FileAttachmentService(
        IFileAttachmentRepository fileAttachmentRepository,
        ILogger<FileAttachmentService> logger)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FileBase64Data?> GetBase64Async(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
            if (fileAttachment == null) return null;
            if (!File.Exists(fileAttachment.FilePath)) return null;
            
            var bytes = await File.ReadAllBytesAsync(fileAttachment.FilePath, cancellationToken);
            var base64 = Convert.ToBase64String(bytes);
            
            return new FileBase64Data
            {
                Base64Content = base64,
                ContentType = fileAttachment.ContentType,
                FileName = fileAttachment.FileName,
                FileType = fileAttachment.FileType.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting base64 for file {FileId}", fileId);
            return null;
        }
    }
}
