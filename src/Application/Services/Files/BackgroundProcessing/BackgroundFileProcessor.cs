using Application.Abstractions.Interfaces;
using Application.Services.AI.RequestHandling.Models;
using Application.Services.Files;
using Domain.Repositories;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Hangfire;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Application.Services.Files.BackgroundProcessing;

public class BackgroundFileProcessor : IBackgroundFileProcessor
{
    private readonly IFileStorageService _fileStorageService;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<BackgroundFileProcessor> _logger;
    private static readonly TimeSpan FileCacheDuration = TimeSpan.FromHours(24);

    private static readonly ActivitySource ActivitySource = new("Application.Services.Files.BackgroundProcessing.BackgroundFileProcessor", "1.0.0");

    public BackgroundFileProcessor(
        IFileStorageService fileStorageService,
        IFileAttachmentRepository fileAttachmentRepository,
        ICacheService cacheService,
        ILogger<BackgroundFileProcessor> logger)
    {
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string GetCacheKey(Guid fileAttachmentId) => $"file-processed:{fileAttachmentId}";

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new int[] { 60, 120, 300 })]
    public async Task ProcessFileAttachmentAsync(Guid fileAttachmentId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(nameof(ProcessFileAttachmentAsync));
        activity?.SetTag("file_attachment.id", fileAttachmentId.ToString());

        FileAttachment? fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileAttachmentId, cancellationToken);
        if (fileAttachment is null)
        {
            _logger.LogWarning("File attachment ID {FileAttachmentId} not found.", fileAttachmentId);
            return;
        }

        try
        {
            await UpdateStatusAsync(fileAttachment, FileProcessingStatus.Processing, cancellationToken);

            byte[] fileBytes;
            string processedContentType = fileAttachment.ContentType;
            long processedFileSize = fileAttachment.FileSize;
            
            bool isImage = fileAttachment.ContentType.StartsWith("image/");

            if (isImage)
            {
                using var processingActivity = ActivitySource.StartActivity("ProcessImageInBackground");
                try
                {
                    (fileBytes, processedContentType, processedFileSize) = await ProcessImageAsync(fileAttachment, cancellationToken);
                    activity?.AddEvent(new ActivityEvent("Image processed successfully."));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process image for attachment {FileAttachmentId}. Storing original file instead.", fileAttachmentId);
                    activity?.AddEvent(new ActivityEvent("Image processing failed, using original file."));
                    fileBytes = await _fileStorageService.ReadFileAsBytesAsync(fileAttachment.FilePath, cancellationToken);
                }
            }
            else
            {
                fileBytes = await _fileStorageService.ReadFileAsBytesAsync(fileAttachment.FilePath, cancellationToken);
            }

            var fileBase64Data = new FileBase64Data
            {
                Base64Content = Convert.ToBase64String(fileBytes),
                ContentType = processedContentType,
                FileName = fileAttachment.FileName,
                FileType = fileAttachment.FileType.ToString()
            };
            
            var cacheKey = GetCacheKey(fileAttachmentId);
            await _cacheService.SetAsync(cacheKey, fileBase64Data, FileCacheDuration, cancellationToken);
            
            fileAttachment.SetProcessedDataCacheKey(cacheKey);
            fileAttachment.UpdateProcessedDetails(processedContentType, processedFileSize);
            await UpdateStatusAsync(fileAttachment, FileProcessingStatus.Ready, cancellationToken);

            _logger.LogInformation("Successfully processed and cached file ID: {FileAttachmentId}", fileAttachmentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file ID: {FileAttachmentId}", fileAttachmentId);
            await UpdateStatusAsync(fileAttachment, FileProcessingStatus.Failed, CancellationToken.None);
            throw;
        }
    }
    
    private async Task<(byte[] fileBytes, string contentType, long fileSize)> ProcessImageAsync(FileAttachment attachment, CancellationToken cancellationToken)
    {
        await using var originalStream = await _fileStorageService.ReadFileAsStreamAsync(attachment.FilePath, cancellationToken);
        using var image = await Image.LoadAsync(originalStream, cancellationToken);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(2048, 2048),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        IImageEncoder encoder = attachment.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => new JpegEncoder { Quality = 75 },
            "image/png" => new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
            "image/webp" => new WebpEncoder { Quality = 80 },
            _ => new JpegEncoder { Quality = 75 }
        };
        
        string newContentType = encoder switch
        {
            JpegEncoder => "image/jpeg",
            PngEncoder => "image/png",
            WebpEncoder => "image/webp",
            _ => "image/jpeg"
        };
        
        await using var memoryStream = new MemoryStream();
        await image.SaveAsync(memoryStream, encoder, cancellationToken);
        var bytes = memoryStream.ToArray();

        return (bytes, newContentType, bytes.Length);
    }

    private async Task UpdateStatusAsync(FileAttachment fileAttachment, FileProcessingStatus status, CancellationToken cancellationToken)
    {
        fileAttachment.SetProcessingStatus(status);
        await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);
    }
} 