using Domain.Aggregates.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable("UserSubscriptions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .IsRequired();

        builder.Property(s => s.SubscriptionPlanId)
            .IsRequired();

        builder.Property(s => s.StartDate)
            .IsRequired();

        builder.Property(s => s.ExpiryDate)
            .IsRequired();

        builder.Property(s => s.CurrentDayUsage)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.LastUsageReset);

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.PaymentReference)
            .HasMaxLength(100);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.SubscriptionPlan)
            .WithMany(p => p.UserSubscriptions)
            .HasForeignKey(s => s.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
