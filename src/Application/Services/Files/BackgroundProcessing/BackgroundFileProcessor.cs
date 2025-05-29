using Application.Abstractions.Interfaces; 
using Application.Services.AI.RequestHandling.Interfaces; 
using Application.Services.AI.RequestHandling.Models; 
using Domain.Repositories; 
using Domain.Aggregates.Chats; 
using Domain.Enums; 
using Microsoft.Extensions.Logging;
using Hangfire;
using System.Diagnostics;

namespace Application.Services.Files.BackgroundProcessing;

public class BackgroundFileProcessor : IBackgroundFileProcessor
{
    private readonly IFileAttachmentService _fileAttachmentService;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<BackgroundFileProcessor> _logger;
    private static readonly TimeSpan FileCacheDuration = TimeSpan.FromHours(24); 

    private static readonly ActivitySource ActivitySource = new("Application.Services.Files.BackgroundProcessing.BackgroundFileProcessor", "1.0.0");

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
        using var activity = ActivitySource.StartActivity(nameof(ProcessFileAttachmentAsync));
        activity?.SetTag("file_attachment.id", fileAttachmentId.ToString());

        FileAttachment? fileAttachment = null;

        try
        {
            fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileAttachmentId, cancellationToken);
            if (fileAttachment == null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "File attachment not found.");
                _logger.LogWarning("File attachment ID {FileAttachmentId} not found. Cannot process.", fileAttachmentId);
                return;
            }
            activity?.SetTag("file.name", fileAttachment.FileName);
            activity?.SetTag("file.path", fileAttachment.FilePath);

            fileAttachment.SetProcessingStatus(FileProcessingStatus.Processing);
            fileAttachment.SetProcessedDataCacheKey(null); 
            await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken); 
            activity?.AddEvent(new ActivityEvent("Status set to Processing."));

            if (string.IsNullOrEmpty(fileAttachment.FilePath) || !System.IO.File.Exists(fileAttachment.FilePath))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "File path invalid or file does not exist.");
                _logger.LogWarning("File path for attachment ID {FileAttachmentId} is invalid or file does not exist: {FilePath}. Setting status to Failed.", 
                                 fileAttachmentId, fileAttachment.FilePath);
                fileAttachment.SetProcessingStatus(FileProcessingStatus.Failed);
                await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);
                activity?.AddEvent(new ActivityEvent("Status set to Failed due to invalid path."));
                return;
            }

            FileBase64Data? fileBase64Data;
            using (var getBase64Activity = ActivitySource.StartActivity("GetBase64Data"))
            {
                fileBase64Data = await _fileAttachmentService.GetBase64Async(fileAttachmentId, cancellationToken);
                getBase64Activity?.SetTag("success", fileBase64Data != null && !string.IsNullOrEmpty(fileBase64Data.Base64Content));
            }

            if (fileBase64Data != null && !string.IsNullOrEmpty(fileBase64Data.Base64Content))
            {
                var cacheKey = GetCacheKey(fileAttachmentId);
                activity?.SetTag("cache.key", cacheKey);
                await _cacheService.SetAsync(cacheKey, fileBase64Data, FileCacheDuration, cancellationToken);
                activity?.AddEvent(new ActivityEvent("File data cached."));
                
                fileAttachment.SetProcessingStatus(FileProcessingStatus.Ready);
                fileAttachment.SetProcessedDataCacheKey(cacheKey);
                await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);
                activity?.AddEvent(new ActivityEvent("Status set to Ready."));

                _logger.LogInformation("Successfully processed file ID: {FileAttachmentId}. Cached with key: {CacheKey}. Content type: {ContentType}, Base64 length: {Length}. Status set to Ready.", 
                                     fileAttachmentId, cacheKey, fileBase64Data.ContentType, fileBase64Data.Base64Content.Length);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Failed to retrieve valid Base64 data.");
                _logger.LogWarning("Failed to retrieve valid Base64 data for file ID: {FileAttachmentId}. Setting status to Failed.", fileAttachmentId);
                fileAttachment.SetProcessingStatus(FileProcessingStatus.Failed);
                await _fileAttachmentRepository.UpdateAsync(fileAttachment, cancellationToken);
                activity?.AddEvent(new ActivityEvent("Status set to Failed due to empty Base64 data."));
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Exception during processing.");
            activity?.AddException(ex);
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
        finally
        {
            activity?.SetTag("file.final_status", fileAttachment?.ProcessingStatus.ToString());
            _logger.LogInformation("Background processing finished for file attachment ID: {FileAttachmentId}. Final status: {Status}", fileAttachmentId, fileAttachment?.ProcessingStatus);
        }
    }
} 