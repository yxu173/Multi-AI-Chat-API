using System;
using System.Collections.Generic;
using Domain.Common;
using Domain.Enums;

namespace Domain.Aggregates.Chats;

public sealed class ChatSession : BaseAuditableEntity
{
    public Guid UserId { get; private set; }
    public string Title { get; private set; }
    public ModelType ModelType { get; private set; }
    private readonly List<Message> _messages = new();
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();

    private ChatSession() { }

    public static ChatSession Create(Guid userId, string modelType)
    {
        var modelTypeResult = Enum.Parse<ModelType>(modelType);
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (!Enum.IsDefined(typeof(ModelType), modelTypeResult)) throw new ArgumentException("Invalid ModelType.", nameof(modelType));

        return new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "New Chat",
            ModelType = modelTypeResult,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddMessage(Message message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        _messages.Add(message);
    }

    public void UpdateTitle(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) throw new ArgumentException("Title cannot be empty.", nameof(newTitle));
        Title = newTitle.Trim();
        LastModifiedAt = DateTime.UtcNow;
    }
}