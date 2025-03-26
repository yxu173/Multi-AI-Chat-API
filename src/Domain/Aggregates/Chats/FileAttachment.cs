using Domain.Common;

namespace Domain.Aggregates.Chats;

public sealed class FileAttachment : BaseEntity
{
    public Guid? MessageId { get; private set; }
    public string FileName { get; private set; }
    public string FilePath { get; private set; }
    public string ContentType { get; private set; }
    public FileType FileType { get; private set; }
    public long FileSize { get; private set; }
    public string? Base64Content { get; private set; }

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
            FileSize = fileSize
        };
    }

    public static FileAttachment CreateWithBase64(string fileName, string filePath, string contentType,
        long fileSize, string base64Content, Guid? messageId = null)
    {
        var attachment = Create(fileName, filePath, contentType, fileSize, messageId);
        attachment.Base64Content = base64Content;
        return attachment;
    }

    public void LinkToMessage(Guid messageId)
    {
        MessageId = messageId;
    }

    public void SetBase64Content(string base64Content)
    {
        if (string.IsNullOrWhiteSpace(base64Content))
            throw new ArgumentException("Base64 content cannot be empty", nameof(base64Content));

        Base64Content = base64Content;
    }

    private static FileType DetermineFileType(string contentType)
    {
        if (contentType.StartsWith("image/"))
            return FileType.Image;
        if (contentType.StartsWith("video/"))
            return FileType.Video;
        if (contentType.StartsWith("audio/"))
            return FileType.Audio;
        if (contentType == "application/pdf")
            return FileType.PDF;
        if (contentType.Contains("text/") || contentType.Contains("document") || contentType.Contains("sheet"))
            return FileType.Document;
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
    Other
}