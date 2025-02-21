using Domain.Common;

namespace Domain.Aggregates.Chats;

public sealed class FileAttachment : BaseEntity
{
    public Guid MessageId { get; private set; }
    public string FileName { get; private set; }
    public string FilePath { get; private set; }

    private FileAttachment() { }

    public static FileAttachment Create(Guid messageId, string fileName, string filePath)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId cannot be empty.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("FileName cannot be empty.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("FilePath cannot be empty.", nameof(filePath));

        return new FileAttachment
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            FileName = fileName,
            FilePath = filePath
        };
    }
}