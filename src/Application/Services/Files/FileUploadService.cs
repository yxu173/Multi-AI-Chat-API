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
using System.IO;

namespace Application.Services.Files;

public class FileUploadService
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly string _uploadsBasePath;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<FileUploadService> _logger;
    private const int StreamBufferSize = 81920; // 80 KB buffer for streaming

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
        var uploadPath = Path.Combine(_uploadsBasePath, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        var originalFileName = Path.GetFileName(file.FileName);
        var uniqueFileName = $"{Guid.NewGuid()}_{originalFileName}";
        var filePath = Path.Combine(uploadPath, uniqueFileName);

        long finalFileSize = 0;
        string contentType = file.ContentType;
        bool processedAsImage = false;

        if (contentType.StartsWith("image/"))
        {
            _logger.LogInformation("Attempting to process image file {FileName} for upload.", originalFileName);
            try
            {
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
                _logger.LogInformation("Image {FileName} processed and saved. Original size: {OriginalSize}, New size: {NewSize}", originalFileName, file.Length, finalFileSize);
            }
            catch (SixLabors.ImageSharp.UnknownImageFormatException ex)
            {
                 _logger.LogWarning(ex, "Could not determine image format for {FileName}. Will save as original.", originalFileName);
            }
            catch (SixLabors.ImageSharp.ImageFormatException ex)
            {
                 _logger.LogWarning(ex, "Invalid image format for {FileName}. Will save as original.", originalFileName);
            }
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Error processing image {FileName}. Will save as original.", originalFileName);
                if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); 
            }
        }

        if (!processedAsImage) 
        {
            _logger.LogInformation("Streaming file {FileName} directly to disk. Path: {FilePath}", originalFileName, filePath);
            await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize, useAsync: true))
            {
                await using var uploadStream = file.OpenReadStream();
                await uploadStream.CopyToAsync(fileStream, StreamBufferSize, cancellationToken);
                finalFileSize = fileStream.Length; 
            }
            _logger.LogInformation("File {FileName} streamed directly to disk. Size: {FileSize}", originalFileName, finalFileSize);
        }
        
        var fileAttachment = FileAttachment.Create(
            originalFileName, 
            filePath,      
            contentType,   
            finalFileSize, 
            messageId
        );

        await _fileAttachmentRepository.AddAsync(fileAttachment, cancellationToken);
        _logger.LogInformation("File attachment {FileAttachmentId} for {FileName} saved to DB. Path: {FilePath}", fileAttachment.Id, fileAttachment.FileName, fileAttachment.FilePath);

        _backgroundJobClient.Enqueue<IBackgroundFileProcessor>(processor => 
            processor.ProcessFileAttachmentAsync(fileAttachment.Id, CancellationToken.None));

        _logger.LogInformation("Enqueued background processing job for file attachment ID: {FileAttachmentId}", fileAttachment.Id);

        return fileAttachment;
    }
    public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
        if (fileAttachment == null)
        {
            return; 
        }

        if (System.IO.File.Exists(fileAttachment.FilePath))
        {
            System.IO.File.Delete(fileAttachment.FilePath);
        }

        // Remove from database
        await _fileAttachmentRepository.DeleteAsync(fileId, cancellationToken);
    }
} 