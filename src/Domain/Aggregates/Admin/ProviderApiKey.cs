using System;
using System.Text.Json.Serialization;
using Domain.Aggregates.Chats;
using Domain.Common;

namespace Domain.Aggregates.Admin;

public sealed class ProviderApiKey : BaseEntity
{
    public Guid AiProviderId { get; private set; }
    public string Secret { get; private set; }
    public string Label { get; private set; }
    public bool IsActive { get; private set; }
    public int DailyQuota { get; private set; }
    public int DailyUsage { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? LastUsed { get; private set; }

    [JsonIgnore]
    public AiProvider AiProvider { get; private set; }

    private ProviderApiKey()
    {
    }

    public static ProviderApiKey Create(
        Guid aiProviderId, 
        string secret, 
        string label, 
        Guid createdByUserId,
        int dailyQuota = 1000)
    {
        if (aiProviderId == Guid.Empty)
            throw new ArgumentException("AiProviderId cannot be empty.", nameof(aiProviderId));
        
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret cannot be empty.", nameof(secret));

        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId cannot be empty.", nameof(createdByUserId));

        return new ProviderApiKey
        {
            Id = Guid.NewGuid(),
            AiProviderId = aiProviderId,
            Secret = secret,
            Label = string.IsNullOrWhiteSpace(label) ? $"Key created on {DateTime.UtcNow:yyyy-MM-dd}" : label,
            IsActive = true,
            DailyQuota = dailyQuota,
            DailyUsage = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public void UpdateSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret cannot be empty.", nameof(secret));

        Secret = secret;
    }

    public void UpdateLabel(string label)
    {
        Label = label;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public void UpdateUsage()
    {
        DailyUsage++;
        LastUsed = DateTime.UtcNow;
    }

    public void ResetDailyUsage()
    {
        DailyUsage = 0;
    }

    public void SetDailyQuota(int quota)
    {
        if (quota < 0)
            throw new ArgumentException("Quota cannot be negative.", nameof(quota));

        DailyQuota = quota;
    }

    public bool HasAvailableQuota()
    {
        return DailyUsage < DailyQuota;
    }
}
