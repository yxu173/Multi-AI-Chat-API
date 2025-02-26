using System;
using System.Collections.Generic;
using Domain.Common;
using Domain.DomainErrors;
using Domain.Enums;
using SharedKernel;

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
        
        if (userId == Guid.Empty) Result.Failure(ChatErrors.UserIdNotValid);

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
        _messages.Add(message);
    }

    public void UpdateTitle(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) throw new ArgumentException("Title cannot be empty.", nameof(newTitle));
        Title = newTitle;
        LastModifiedAt = DateTime.UtcNow;
    }
}