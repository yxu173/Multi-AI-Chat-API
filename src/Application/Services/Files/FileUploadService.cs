using Domain.Aggregates.Chats;
using Domain.Common;
using Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Hangfire;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Application.Services.Files.BackgroundProcessing;

namespace Application.Services.Files;

public class FileUploadService
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<FileUploadService> _logger;

    private static readonly ActivitySource ActivitySource = new("Application.Services.Files.FileUploadService", "1.0.0");

    public FileUploadService(
        IFileAttachmentRepository fileAttachmentRepository,
        IFileStorageService fileStorageService,
        IBackgroundJobClient backgroundJobClient,
        ILogger<FileUploadService> logger)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FileAttachment> UploadFileAsync(IFormFile file, Guid? messageId = null, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(UploadFileAsync));
        activity?.SetTag("file.original_name", file.FileName);
        activity?.SetTag("file.content_type", file.ContentType);
        activity?.SetTag("file.original_size", file.Length);
        activity?.SetTag("message.id", messageId?.ToString());
        
        var originalFileName = Path.GetFileName(file.FileName);

        string filePath;
        await using (var uploadStream = file.OpenReadStream())
        {
            filePath = await _fileStorageService.SaveFileAsync(uploadStream, originalFileName, cancellationToken);
        }
        activity?.SetTag("file.path", filePath);
        
        var fileAttachment = FileAttachment.Create(
            originalFileName,
            filePath,
            file.ContentType,
            file.Length,
            messageId
        );
        activity?.SetTag("file_attachment.id", fileAttachment.Id.ToString());

        await _fileAttachmentRepository.AddAsync(fileAttachment, cancellationToken);
        activity?.AddEvent(new ActivityEvent("File attachment saved to DB."));

        var jobId = _backgroundJobClient.Enqueue<IBackgroundFileProcessor>(x => x.ProcessFileAttachmentAsync(fileAttachment.Id, default));
        activity?.AddEvent(new ActivityEvent("Enqueued background file processing.", tags: new ActivityTagsCollection { { "hangfire.job_id", jobId } }));

        _logger.LogInformation("File {FileName} uploaded and enqueued for processing. Attachment ID: {AttachmentId}", originalFileName, fileAttachment.Id);

        return fileAttachment;
    }
    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(DeleteFileAsync));
        activity?.SetTag("file_attachment.id", fileId.ToString());

        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
        if (fileAttachment == null)
        {
            activity?.AddEvent(new ActivityEvent("File attachment not found for deletion."));
            return;
        }
        activity?.SetTag("file.path", fileAttachment.FilePath);

        await _fileStorageService.DeleteFileAsync(fileAttachment.FilePath, cancellationToken);
        activity?.AddEvent(new ActivityEvent("Physical file deleted via storage service."));
        
        await _fileAttachmentRepository.DeleteAsync(fileId, cancellationToken);
        activity?.AddEvent(new ActivityEvent("File attachment record deleted from DB."));
    }
} 