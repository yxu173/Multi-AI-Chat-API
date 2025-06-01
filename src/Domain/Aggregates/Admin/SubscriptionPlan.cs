using System;
using System.Collections.Generic;
using Domain.Common;

namespace Domain.Aggregates.Admin;

public sealed class SubscriptionPlan : BaseEntity
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public double MaxRequestsPerMonth { get; private set; }
    public int MaxTokensPerRequest { get; private set; }
    public decimal MonthlyPrice { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastModified { get; private set; }

    // Inverse navigation property
    private readonly List<UserSubscription> _userSubscriptions = new();
    public IReadOnlyList<UserSubscription> UserSubscriptions => _userSubscriptions.AsReadOnly();

    private SubscriptionPlan()
    {
    }

    public static SubscriptionPlan Create(
        string name,
        string description,
        double maxRequestsPerMonth,
        int maxTokensPerRequest,
        decimal monthlyPrice)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        if (maxRequestsPerMonth <= 0)
            throw new ArgumentException("MaxRequestsPerMonth must be positive.", nameof(maxRequestsPerMonth));

        if (maxTokensPerRequest <= 0)
            throw new ArgumentException("MaxTokensPerRequest must be positive.", nameof(maxTokensPerRequest));

        if (monthlyPrice < 0)
            throw new ArgumentException("MonthlyPrice cannot be negative.", nameof(monthlyPrice));

        return new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            MaxRequestsPerMonth = maxRequestsPerMonth,
            MaxTokensPerRequest = maxTokensPerRequest,
            MonthlyPrice = monthlyPrice,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static SubscriptionPlan CreateFreeTier()
    {
        return Create("Free Tier", "Basic free plan with 100 monthly requests and standard token limits", 100.0, 1000, 0.0m);
    }

    public static SubscriptionPlan CreatePremiumTier()
    {
        return Create("Premium Tier", "Advanced plan with 1000 monthly requests and higher token limits", 1000.0, 5000, 19.99m);
    }

    public void Update(
        string name,
        string description,
        double maxRequestsPerMonth,
        int maxTokensPerRequest,
        decimal monthlyPrice)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        if (maxRequestsPerMonth <= 0)
            throw new ArgumentException("MaxRequestsPerMonth must be positive.", nameof(maxRequestsPerMonth));

        if (maxTokensPerRequest <= 0)
            throw new ArgumentException("MaxTokensPerRequest must be positive.", nameof(maxTokensPerRequest));

        if (monthlyPrice < 0)
            throw new ArgumentException("MonthlyPrice cannot be negative.", nameof(monthlyPrice));

        Name = name;
        Description = description;
        MaxRequestsPerMonth = maxRequestsPerMonth;
        MaxTokensPerRequest = maxTokensPerRequest;
        MonthlyPrice = monthlyPrice;
        LastModified = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        LastModified = DateTime.UtcNow;
    }
}
