using System;
using System.Text.Json.Serialization;
using Domain.Aggregates.Users;
using Domain.Common;

namespace Domain.Aggregates.Admin;

public sealed class UserSubscription : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid SubscriptionPlanId { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime ExpiryDate { get; private set; }
    public double CurrentMonthUsage { get; private set; }
    public DateTime? LastUsageReset { get; private set; }
    public bool IsActive { get; private set; }
    public string? PaymentReference { get; private set; }

    [JsonIgnore]
    public User User { get; private set; }
    
    [JsonIgnore]
    public SubscriptionPlan SubscriptionPlan { get; private set; }

    private UserSubscription()
    {
    }

    public static UserSubscription Create(
        Guid userId,
        Guid subscriptionPlanId,
        DateTime startDate,
        DateTime expiryDate,
        string? paymentReference = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));

        if (subscriptionPlanId == Guid.Empty)
            throw new ArgumentException("SubscriptionPlanId cannot be empty.", nameof(subscriptionPlanId));

        if (startDate >= expiryDate)
            throw new ArgumentException("ExpiryDate must be after StartDate.", nameof(expiryDate));

        return new UserSubscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SubscriptionPlanId = subscriptionPlanId,
            StartDate = startDate,
            ExpiryDate = expiryDate,
            CurrentMonthUsage = 0.0,
            LastUsageReset = DateTime.UtcNow,
            IsActive = true,
            PaymentReference = paymentReference
        };
    }

    public void Extend(DateTime newExpiryDate)
    {
        if (newExpiryDate <= ExpiryDate)
            throw new ArgumentException("New expiry date must be after current expiry date.", nameof(newExpiryDate));

        ExpiryDate = newExpiryDate;
    }

    public void UpdatePlan(Guid newPlanId)
    {
        if (newPlanId == Guid.Empty)
            throw new ArgumentException("New plan ID cannot be empty.", nameof(newPlanId));

        SubscriptionPlanId = newPlanId;
    }

    public void IncrementUsage(double cost)
    {
        EnsureUsageResetIfNeeded();
        CurrentMonthUsage += cost;
    }

    public void ResetMonthlyUsage()
    {
        CurrentMonthUsage = 0.0;
        LastUsageReset = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public bool HasAvailableQuota(double planMaxRequests)
    {
        EnsureUsageResetIfNeeded();
        return CurrentMonthUsage < planMaxRequests;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow > ExpiryDate;
    }

    private void EnsureUsageResetIfNeeded()
    {
        var now = DateTime.UtcNow;
        if (LastUsageReset == null || LastUsageReset?.Year != now.Year || LastUsageReset?.Month != now.Month)
        {
            ResetMonthlyUsage();
        }
    }
}
