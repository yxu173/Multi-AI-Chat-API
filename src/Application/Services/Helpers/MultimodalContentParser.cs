using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Application.Abstractions.Interfaces; // For ICacheService
using Domain.Repositories; // For IFileAttachmentRepository
using Domain.Aggregates.Chats; // For FileAttachment
using Domain.Enums; // For FileProcessingStatus
using Application.Services.AI.RequestHandling.Models; // For FileBase64Data

namespace Application.Services.Helpers;

public record ContentPart;
public record TextPart(string Text) : ContentPart;
public record ImagePart(string MimeType, string Base64Data, string? FileName = null) : ContentPart;
public record FilePart(string MimeType, string Base64Data, string FileName) : ContentPart;


public class MultimodalContentParser
{
    private readonly ILogger<MultimodalContentParser> _logger;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly ICacheService _cacheService;

    // Regex for existing embedded base64 format: <image-base64:[filename_optional]:mime/type;base64,DATA> or <file-base64:filename:mime/type;base64,DATA>
    private static readonly Regex EmbeddedBase64Regex =
        new Regex(@"<(image|file)-base64:(?:([^:]*?):)?([^;]*?);base64,([^>]*?)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // Regex for new placeholder format: <image:guid>
    private static readonly Regex ImagePlaceholderRegex =
        new Regex(@"<image:([0-9A-Fa-f]{8}-(?:[0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12})>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex for new placeholder format: <file:guid:filename>
    // Allows filename to contain most characters, including spaces, but not '>'
    private static readonly Regex FilePlaceholderRegex =
        new Regex(@"<file:([0-9A-Fa-f]{8}-(?:[0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}):([^>]+)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


    public MultimodalContentParser(
        ILogger<MultimodalContentParser> logger,
        IFileAttachmentRepository fileAttachmentRepository,
        ICacheService cacheService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<List<ContentPart>> ParseAsync(string messageContent, CancellationToken cancellationToken = default)
    {
        var contentParts = new List<ContentPart>();
        if (string.IsNullOrEmpty(messageContent)) return contentParts;
        
        var lastIndex = 0;
        var tempParts = new List<(int Index, int Length, ContentPart Part)>();

        // 1. Process new image placeholders: <image:guid>
        foreach (Match match in ImagePlaceholderRegex.Matches(messageContent))
        {
            if (!Guid.TryParse(match.Groups[1].Value, out var fileId))
            {
                _logger.LogWarning("Invalid GUID in image placeholder: {Placeholder}", match.Value);
                // Potentially add as plain text or skip
                continue;
            }
            var fileData = await GetFileDataAsync(fileId, null, cancellationToken);
            tempParts.Add((match.Index, match.Length, fileData ?? new TextPart($"[Image attachment {fileId} not found or not ready]")));
        }

        // 2. Process new file placeholders: <file:guid:filename>
        foreach (Match match in FilePlaceholderRegex.Matches(messageContent))
        {
            if (!Guid.TryParse(match.Groups[1].Value, out var fileId))
            {
                _logger.LogWarning("Invalid GUID in file placeholder: {Placeholder}", match.Value);
                continue;
            }
            string fileName = match.Groups[2].Value;
            var fileData = await GetFileDataAsync(fileId, fileName, cancellationToken);
            tempParts.Add((match.Index, match.Length, fileData ?? new TextPart($"[File attachment '{fileName}' ({fileId}) not found or not ready]")));
        }
        
        // 3. Process existing embedded base64 tags
        foreach (Match match in EmbeddedBase64Regex.Matches(messageContent))
        {
            string tagType = match.Groups[1].Value.ToLowerInvariant();
            string? potentialFileName = match.Groups[2].Value;
            string? potentialMimeType = match.Groups[3].Value;
            string base64Data = match.Groups[4].Value;

            if (string.IsNullOrWhiteSpace(base64Data) || string.IsNullOrEmpty(potentialMimeType) || !potentialMimeType.Contains("/"))
            {
                _logger.LogWarning("Malformed embedded base64 tag (missing data or invalid MIME type \'{MimeType}\'): {Tag}", potentialMimeType, match.Value);
                tempParts.Add((match.Index, match.Length, new TextPart(match.Value))); // Add as plain text
                continue;
            }
            
            string mimeType = potentialMimeType.Trim();

            if (tagType == "image")
            {
                tempParts.Add((match.Index, match.Length, new ImagePart(mimeType, base64Data, potentialFileName)));
            }
            else if (tagType == "file")
            {
                string? fileName = potentialFileName?.Trim();
                if (!string.IsNullOrEmpty(fileName))
                {
                    tempParts.Add((match.Index, match.Length, new FilePart(mimeType, base64Data, fileName)));
                }
                else
                {
                     _logger.LogWarning("Malformed embedded file tag (missing filename): {Tag}", match.Value);
                    tempParts.Add((match.Index, match.Length, new TextPart(match.Value))); // Add as plain text
                }
            }
        }

        // Sort all found parts by their original index in the message
        tempParts.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Reconstruct the message with text parts and resolved content parts
        foreach (var tempPart in tempParts)
        {
            if (tempPart.Index > lastIndex)
            {
                string textBefore = messageContent.Substring(lastIndex, tempPart.Index - lastIndex);
                if (!string.IsNullOrWhiteSpace(textBefore)) contentParts.Add(new TextPart(textBefore.Trim()));
            }
            contentParts.Add(tempPart.Part);
            lastIndex = tempPart.Index + tempPart.Length;
        }

        if (lastIndex < messageContent.Length)
        {
            string textAfter = messageContent.Substring(lastIndex);
            if (!string.IsNullOrWhiteSpace(textAfter)) contentParts.Add(new TextPart(textAfter.Trim()));
        }

        // If parsing resulted in no actual content parts, and original message wasn't empty, return original as text
        if (!contentParts.Any() && !string.IsNullOrWhiteSpace(messageContent))
        {
            _logger.LogDebug("Multimodal parsing resulted in no parts for non-empty content, returning original content as a single TextPart.");
            return new List<ContentPart> { new TextPart(messageContent) };
        }
        
        // Remove empty text parts that might result from trimming or if all placeholders failed
        contentParts.RemoveAll(p => p is TextPart tp && string.IsNullOrWhiteSpace(tp.Text));

        // Final check: if only one text part remains and it's the original message (e.g. no placeholders found/processed), return it
        if (contentParts.Count == 1 && contentParts[0] is TextPart finalSingleTextPart && finalSingleTextPart.Text == messageContent.Trim())
        {
             return contentParts; // It's already trimmed
        }
        
        // Combine consecutive text parts (again, after all resolving)
        return CombineConsecutiveTextParts(contentParts);
    }

    private async Task<ContentPart?> GetFileDataAsync(Guid fileId, string? specifiedFileName, CancellationToken cancellationToken)
    {
        try
        {
            var fileAttachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
            if (fileAttachment == null)
            {
                _logger.LogWarning("FileAttachment not found for ID: {FileId}", fileId);
                return new TextPart($"[Attachment {fileId} not found]");
            }

            string displayName = specifiedFileName ?? fileAttachment.FileName;

            switch (fileAttachment.ProcessingStatus)
            {
                case FileProcessingStatus.Pending:
                case FileProcessingStatus.Processing:
                    _logger.LogInformation("File ID {FileId} ('{FileName}') is still processing.", fileId, displayName);
                    return new TextPart($"[File '{displayName}' is still processing. Please try again shortly.]");
                case FileProcessingStatus.Failed:
                    _logger.LogError("File ID {FileId} ('{FileName}') failed processing.", fileId, displayName);
                    return new TextPart($"[File '{displayName}' could not be processed.]");
                case FileProcessingStatus.Ready:
                    if (string.IsNullOrEmpty(fileAttachment.ProcessedDataCacheKey))
                    {
                        _logger.LogError("File ID {FileId} ('{FileName}') is Ready but ProcessedDataCacheKey is missing.", fileId, displayName);
                        return new TextPart($"[Error retrieving processed data for '{displayName}'.]");
                    }
                    var cachedData = await _cacheService.GetAsync<FileBase64Data>(fileAttachment.ProcessedDataCacheKey, cancellationToken);
                    if (cachedData == null || string.IsNullOrEmpty(cachedData.Base64Content))
                    {
                        _logger.LogWarning("Processed data for File ID {FileId} ('{FileName}') not found in cache (key: {CacheKey}) or content empty. It might have expired or failed to cache.", 
                                         fileId, displayName, fileAttachment.ProcessedDataCacheKey);
                        return new TextPart($"[Processed data for '{displayName}' is unavailable. It may have expired from cache or an error occurred.]");
                    }
                    _logger.LogInformation("Successfully retrieved cached data for File ID {FileId} ('{FileName}') for multimodal parsing.", fileId, displayName);
                    if (fileAttachment.FileType == FileType.Image)
                    {
                        return new ImagePart(cachedData.ContentType, cachedData.Base64Content, displayName);
                    }
                    return new FilePart(cachedData.ContentType, cachedData.Base64Content, displayName);
                default:
                    _logger.LogWarning("Unknown FileProcessingStatus {Status} for File ID {FileId} ('{FileName}')", fileAttachment.ProcessingStatus, fileId, displayName);
                    return new TextPart($"[Attachment '{displayName}' has an unknown status.]");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file data for ID: {FileId}", fileId);
            return new TextPart($"[Error accessing attachment data for {fileId}]");
        }
    }
    
    private List<ContentPart> CombineConsecutiveTextParts(List<ContentPart> parts)
    {
        if (!parts.Any()) return parts;

        var combined = new List<ContentPart>();
        StringBuilder? currentText = null;

        foreach (var part in parts)
        {
            if (part is TextPart tp)
            {
                if (currentText == null) currentText = new StringBuilder();
                // Add a space if currentText is not empty and doesn't end with space, 
                // and tp.Text doesn't start with space, to avoid merging words.
                // However, simple concatenation is often preferred for direct content parts.
                // Let's be careful not to add too many spaces.
                // For now, just append. Trimming happens at the end of ParseAsync or when TextParts are created.
                currentText.Append(tp.Text); 
            }
            else
            {
                if (currentText != null && currentText.Length > 0)
                {
                    var trimmedText = currentText.ToString().Trim();
                    if(!string.IsNullOrEmpty(trimmedText)) combined.Add(new TextPart(trimmedText));
                    currentText = null;
                }
                combined.Add(part);
            }
        }
        if (currentText != null && currentText.Length > 0)
        {
            var trimmedText = currentText.ToString().Trim();
            if(!string.IsNullOrEmpty(trimmedText)) combined.Add(new TextPart(trimmedText));
        }
        return combined;
    }
} 