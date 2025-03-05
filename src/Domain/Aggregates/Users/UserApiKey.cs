using System;
using Domain.Aggregates.Chats;

namespace Domain.Aggregates.Users;

public sealed class UserApiKey
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid AiProviderId { get; private set; }
    public string ApiKey { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsed { get; private set; }


    public User User { get; private set; }
    public AiProvider AiProvider { get; private set; }

    private UserApiKey()
    {
    }

    public static UserApiKey Create(Guid userId, Guid aiProviderId, string apiKey)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (aiProviderId == Guid.Empty)
            throw new ArgumentException("AiProviderId cannot be empty.", nameof(aiProviderId));
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("ApiKey cannot be empty.", nameof(apiKey));

        return new UserApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AiProviderId = aiProviderId,
            ApiKey = apiKey,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateApiKey(string apiKey)
    {
        ApiKey = apiKey;
    }

    public void UpdateLastUsed()
    {
        LastUsed = DateTime.UtcNow;
    }
}