using System;
using Domain.Common;
using Domain.Enums;
using SharedKernel;

namespace Domain.Aggregates.Chats;

public sealed class Message : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Content { get; private set; }
    public bool IsFromAi { get; private set; }
    public Guid ChatSessionId { get; private set; }
    public ChatSession ChatSession { get; private set; }
    public MessageStatus Status { get; private set; }
    private readonly List<FileAttachment> _fileAttachments = new();
    public IReadOnlyList<FileAttachment> FileAttachments => _fileAttachments.AsReadOnly();

    private Message()
    {
    }

    public static Message CreateUserMessage(Guid userId, Guid chatSessionId, string content, IEnumerable<FileAttachment>? fileAttachments = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (chatSessionId == Guid.Empty)
            throw new ArgumentException("ChatSessionId cannot be empty.", nameof(chatSessionId));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty.", nameof(content));

        var message = new Message
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            ChatSessionId = chatSessionId,
            IsFromAi = false,
            Status = MessageStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        return message;
    }

    public static Message CreateAiMessage(Guid userId, Guid chatSessionId)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (chatSessionId == Guid.Empty)
            throw new ArgumentException("ChatSessionId cannot be empty.", nameof(chatSessionId));

        return new Message
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = string.Empty,
            ChatSessionId = chatSessionId,
            IsFromAi = true,
            Status = MessageStatus.Streaming,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    public void UpdateContent(string newContent)
    {
        Content = newContent ?? string.Empty;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void AppendContent(string chunk)
    {
        Content += chunk;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void CompleteMessage()
    {
        Status = MessageStatus.Completed;
        LastModifiedAt = DateTime.UtcNow;
    }
    public void InterruptMessage()
    {
        Status = MessageStatus.Interrupted;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void FailMessage()
    {
        Status = MessageStatus.Failed;
        LastModifiedAt = DateTime.UtcNow;
    }

    public void AddFileAttachment(FileAttachment fileAttachment)
    {
        if (fileAttachment == null) throw new ArgumentNullException(nameof(fileAttachment));
        _fileAttachments.Add(fileAttachment);
    }

    public void ClearFileAttachments()
    {
        _fileAttachments.Clear();
    }
}