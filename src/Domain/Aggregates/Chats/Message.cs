using System;
using Domain.Common;
using SharedKernel;

namespace Domain.Aggregates.Chats;

public sealed class Message : BaseAuditableEntity
{
    public string Content { get; private set; }
    public MessageRole Role { get; private set; }
    public Guid ChatSessionId { get; private set; }
    public ChatSession ChatSession { get; private set; }
    public MessageStatus Status { get; private set; }

    private Message()
    {
    }

    public static Message Create(string content, MessageRole role, Guid chatSessionId)
    {
        return new Message
        {
            Id = Guid.NewGuid(),
            Content = content,
            Role = role,
            ChatSessionId = chatSessionId,
            Status = MessageStatus.Completed
        };
    }

    public void UpdateContent(string content)
    {
        Content = content;
        Status = MessageStatus.Completed;
    }

    public void UpdateStatus(MessageStatus status)
    {
        Status = status;
    }

    public enum MessageStatus
    {
        Pending,
        Streaming,
        Completed,
        Failed
    }

    public enum MessageRole
    {
        User,
        Assistant,
        System
    }
}