using Application.Abstractions.Interfaces; 
using Application.Services.AI.RequestHandling.Interfaces; 
using Application.Services.AI.RequestHandling.Models; 
using Domain.Repositories; 
using Domain.Aggregates.Chats; 
using Domain.Enums; 
using Microsoft.Extensions.Logging;
using Hangfire;

namespace Application.Services.Files.BackgroundProcessing;

public class BackgroundFileProcessor : IBackgroundFileProcessor
{
    private readonly IFileAttachmentService _fileAttachmentService;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<BackgroundFileProcessor> _logger;
    private static readonly TimeSpan FileCacheDuration = TimeSpan.FromHours(24); 

    public BackgroundFileProcessor(
        IFileAttachmentService fileAttachmentService,
        IFileAttachmentRepository fileAttachmentRepository,
        ICacheService cacheService,
        ILogger<BackgroundFileProcessor> logger)
    {
        _fileAttachmentService = fileAttachmentService ?? throw new ArgumentNullException(nameof(fileAttachmentService));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string GetCacheKey(Guid fileAttachmentId) => $"file-processed:{fileAttachmentId}";

  
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new int[] { 60, 120, 300 })] 
    public async Task ProcessFileAttachmentAsync(Guid fileAttachmentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Background processing started for file attachment ID: {FileAttachmentId}", fileAttachmentId);
        FileAttachment? fileAttachment = null;

        try
        {
            fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileAttachmentId, cancellationToken);
            if (fileAttachment == null)
            {
                _logger.LogWarning("File attachment ID {FileAttachmentId} not found. Cannot process.", fileAttachmentId);
                return;
            }

           
            fileAttachment.SetProcessingStatus(FileProcessingStatus.Processing);
            fileAttachment.SetProcessedDataCacheKey(null); 
            await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken); 
            _logger.LogDebug("File attachment {FileAttachmentId} status set to Processing.", fileAttachmentId);

            if (string.IsNullOrEmpty(fileAttachment.FilePath) || !System.IO.File.Exists(fileAttachment.FilePath))
            {
                _logger.LogWarning("File path for attachment ID {FileAttachmentId} is invalid or file does not exist: {FilePath}. Setting status to Failed.", 
                                 fileAttachmentId, fileAttachment.FilePath);
                fileAttachment.SetProcessingStatus(FileProcessingStatus.Failed);
                await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);
                return;
            }

            FileBase64Data? fileBase64Data = await _fileAttachmentService.GetBase64Async(fileAttachmentId, cancellationToken);

            if (fileBase64Data != null && !string.IsNullOrEmpty(fileBase64Data.Base64Content))
            {
                var cacheKey = GetCacheKey(fileAttachmentId);
                await _cacheService.SetAsync(cacheKey, fileBase64Data, FileCacheDuration, cancellationToken);
                
                fileAttachment.SetProcessingStatus(FileProcessingStatus.Ready);
                fileAttachment.SetProcessedDataCacheKey(cacheKey);
                await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);

                _logger.LogInformation("Successfully processed file ID: {FileAttachmentId}. Cached with key: {CacheKey}. Content type: {ContentType}, Base64 length: {Length}. Status set to Ready.", 
                                     fileAttachmentId, cacheKey, fileBase64Data.ContentType, fileBase64Data.Base64Content.Length);
            }
            else
            {
                _logger.LogWarning("Failed to retrieve valid Base64 data for file ID: {FileAttachmentId}. Setting status to Failed.", fileAttachmentId);
                fileAttachment.SetProcessingStatus(FileProcessingStatus.Failed);
                await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during background processing of file ID: {FileAttachmentId}. Setting status to Failed.", fileAttachmentId);
            if (fileAttachment != null)
            {
                try
                {
                    fileAttachment.SetProcessingStatus(FileProcessingStatus.Failed);
                    await _fileAttachmentRepository.UpdateAsync(fileAttachment, CancellationToken.None); 
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "Failed to update file attachment {FileAttachmentId} status to Failed after an error.", fileAttachmentId);
                }
            }
            throw;
        }
        _logger.LogInformation("Background processing finished for file attachment ID: {FileAttachmentId}. Final status: {Status}", fileAttachmentId, fileAttachment?.ProcessingStatus);
    }
} 