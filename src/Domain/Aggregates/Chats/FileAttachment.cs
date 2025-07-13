using Domain.Common;
using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class FileAttachment : BaseEntity
{
    public Guid? MessageId { get; private set; }
    public string FileName { get; private set; }
    public string FilePath { get; private set; }
    public string ContentType { get; private set; }
    public FileType FileType { get; private set; }
    public long FileSize { get; private set; }

    public FileProcessingStatus ProcessingStatus { get; private set; }
    public string? ProcessedDataCacheKey { get; private set; }

    private FileAttachment()
    {
    }

    public static FileAttachment Create(string fileName, string filePath, string contentType,
        long fileSize, Guid? messageId = null)
    {
        return new FileAttachment
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            FileName = fileName,
            FilePath = filePath,
            ContentType = contentType,
            FileType = DetermineFileType(contentType),
            FileSize = fileSize,
            ProcessingStatus = FileProcessingStatus.Pending,
            ProcessedDataCacheKey = null
        };
    }
    
    public void SetProcessingStatus(FileProcessingStatus status)
    {
        ProcessingStatus = status;
    }

    public void SetProcessedDataCacheKey(string? cacheKey)
    {
        ProcessedDataCacheKey = cacheKey;
    }

    public void UpdateProcessedDetails(string newContentType, long newFileSize)
    {
        ContentType = newContentType;
        FileSize = newFileSize;
        FileType = DetermineFileType(newContentType);
    }

    private static FileType DetermineFileType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return FileType.Other;
        
        // Images
        if (contentType.StartsWith("image/"))
            return FileType.Image;
            
        // PDF
        if (contentType == "application/pdf")
            return FileType.PDF;
            
        // CSV
        if (contentType == "text/csv" || contentType == "application/csv")
            return FileType.CSV;
            
        // Plain text
        if (contentType == "text/plain")
            return FileType.Text;
            
        // Other text-based documents (fallback)
        if (contentType.StartsWith("text/"))
            return FileType.Text;
            
        return FileType.Other;
    }
}

public enum FileType
{
    Image,
    Video,
    Audio,
    Document,
    PDF,
    CSV,
    Text,
    Other
}