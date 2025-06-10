using Domain.Aggregates.Chats;
using Domain.Common;
using Domain.Repositories;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using Application.Services.Files.BackgroundProcessing;
using Hangfire;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.Services.Files;

public class FileUploadService
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly string _uploadsBasePath;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<FileUploadService> _logger;
    private const int StreamBufferSize = 81920;

    private static readonly ActivitySource ActivitySource = new("Application.Services.Files.FileUploadService", "1.0.0");

    public FileUploadService(
        IFileAttachmentRepository fileAttachmentRepository,
        string uploadsBasePath,
        IBackgroundJobClient backgroundJobClient,
        ILogger<FileUploadService> logger)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _uploadsBasePath = uploadsBasePath ?? throw new ArgumentNullException(nameof(uploadsBasePath));
        _backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!Directory.Exists(_uploadsBasePath))
        {
            Directory.CreateDirectory(_uploadsBasePath);
        }
    }

    public async Task<FileAttachment> UploadFileAsync(IFormFile file, Guid? messageId = null, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(UploadFileAsync));
        activity?.SetTag("file.original_name", file.FileName);
        activity?.SetTag("file.content_type", file.ContentType);
        activity?.SetTag("file.original_size", file.Length);
        activity?.SetTag("message.id", messageId?.ToString());

        var uploadPath = Path.Combine(_uploadsBasePath, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
            activity?.AddEvent(new ActivityEvent("Created daily upload directory", tags: new ActivityTagsCollection { { "path", uploadPath } }));
        }

        var originalFileName = Path.GetFileName(file.FileName);
        var uniqueFileName = $"{Guid.NewGuid()}_{originalFileName}";
        var filePath = Path.Combine(uploadPath, uniqueFileName);
        activity?.SetTag("file.path", filePath);

        long finalFileSize = 0;
        string contentType = file.ContentType;
        bool processedAsImage = false;

        if (contentType.StartsWith("image/"))
        {
            activity?.AddEvent(new ActivityEvent("Attempting image processing."));
            try
            {
                using var imageProcessingActivity = ActivitySource.StartActivity("ProcessImage");
                using var imageReadStream = file.OpenReadStream();
                using var image = await Image.LoadAsync(imageReadStream, cancellationToken);

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(2048, 2048),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));

                IImageEncoder encoder = contentType.ToLowerInvariant() switch
                {
                    "image/jpeg" or "image/jpg" => new JpegEncoder { Quality = 75 },
                    "image/png" => new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
                    "image/webp" => new WebpEncoder { Quality = 80 },
                    _ => new JpegEncoder { Quality = 75 }
                };

                await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize, useAsync: true))
                {
                    await image.SaveAsync(fileStream, encoder, cancellationToken);
                    finalFileSize = fileStream.Length;
                }

                if (encoder is JpegEncoder) contentType = "image/jpeg";
                else if (encoder is PngEncoder) contentType = "image/png";
                else if (encoder is WebpEncoder) contentType = "image/webp";

                processedAsImage = true;
                imageProcessingActivity?.SetTag("image.processed", true);
                imageProcessingActivity?.SetTag("image.final_size", finalFileSize);
                imageProcessingActivity?.SetTag("image.final_content_type", contentType);
                activity?.AddEvent(new ActivityEvent("Image processed successfully.", tags: new ActivityTagsCollection { { "final_size", finalFileSize }, { "final_content_type", contentType } }));
            }
            catch (UnknownImageFormatException ex)
            {
                activity?.AddEvent(new ActivityEvent("Image processing failed: Unknown format.", tags: new ActivityTagsCollection { { "exception", ex.Message } }));
                _logger.LogWarning(ex, "Could not determine image format for {FileName}. Will save as original.", originalFileName);
            }
            catch (ImageFormatException ex)
            {
                activity?.AddEvent(new ActivityEvent("Image processing failed: Invalid format.", tags: new ActivityTagsCollection { { "exception", ex.Message } }));
                _logger.LogWarning(ex, "Invalid image format for {FileName}. Will save as original.", originalFileName);
            }
            catch (Exception ex)
            {
                activity?.AddEvent(new ActivityEvent("Image processing failed: Generic error.", tags: new ActivityTagsCollection { { "exception", ex.Message } }));
                activity?.AddException(ex);
                _logger.LogError(ex, "Error processing image {FileName}. Will save as original.", originalFileName);
                if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
            }
        }

        if (!processedAsImage)
        {
            activity?.AddEvent(new ActivityEvent("Streaming file directly to disk."));
            await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize, useAsync: true))
            {
                await using var uploadStream = file.OpenReadStream();
                await uploadStream.CopyToAsync(fileStream, StreamBufferSize, cancellationToken);
                finalFileSize = fileStream.Length;
            }
            activity?.AddEvent(new ActivityEvent("File streamed directly.", tags: new ActivityTagsCollection{ {"final_size", finalFileSize} }));
        }
        activity?.SetTag("file.final_size", finalFileSize);
        activity?.SetTag("file.final_content_type", contentType);

        var fileAttachment = FileAttachment.Create(
            originalFileName,
            filePath,
            contentType,
            finalFileSize,
            messageId
        );
        activity?.SetTag("file_attachment.id", fileAttachment.Id.ToString());

        await _fileAttachmentRepository.AddAsync(fileAttachment, cancellationToken);
        activity?.AddEvent(new ActivityEvent("File attachment saved to DB."));

        var jobId = _backgroundJobClient.Enqueue<IBackgroundFileProcessor>(processor =>
            processor.ProcessFileAttachmentAsync(fileAttachment.Id, CancellationToken.None));
        activity?.AddEvent(new ActivityEvent("Enqueued background file processing.", tags: new ActivityTagsCollection { { "hangfire.job_id", jobId } }));

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

        if (File.Exists(fileAttachment.FilePath))
        {
            File.Delete(fileAttachment.FilePath);
            activity?.AddEvent(new ActivityEvent("Physical file deleted from disk."));
        }

        // Remove from database
        await _fileAttachmentRepository.DeleteAsync(fileId, cancellationToken);
        activity?.AddEvent(new ActivityEvent("File attachment record deleted from DB."));
    }
} 