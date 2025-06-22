using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.AI.RequestHandling.Models;
using Application.Services.Files;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.RequestHandling;

public class FileAttachmentService : IFileAttachmentService
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<FileAttachmentService> _logger;

    public FileAttachmentService(
        IFileAttachmentRepository fileAttachmentRepository,
        IFileStorageService fileStorageService,
        ILogger<FileAttachmentService> logger)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FileBase64Data?> GetBase64Async(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
            if (fileAttachment == null) return null;
            
            var bytes = await _fileStorageService.ReadFileAsBytesAsync(fileAttachment.FilePath, cancellationToken);
            var base64 = Convert.ToBase64String(bytes);
            
            return new FileBase64Data
            {
                Base64Content = base64,
                ContentType = fileAttachment.ContentType,
                FileName = fileAttachment.FileName,
                FileType = fileAttachment.FileType.ToString()
            };
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "File not found when getting base64 for file {FileId} at path {FilePath}", fileId, ex.FileName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting base64 for file {FileId}", fileId);
            return null;
        }
    }
}
