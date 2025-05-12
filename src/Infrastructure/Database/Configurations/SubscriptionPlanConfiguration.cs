using Domain.Aggregates.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("SubscriptionPlans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(p => p.MaxRequestsPerDay)
            .IsRequired();

        builder.Property(p => p.MaxTokensPerRequest)
            .IsRequired();

        builder.Property(p => p.MonthlyPrice)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.LastModified);
        
        builder.Navigation(p => p.UserSubscriptions);
        builder.Metadata.FindNavigation(nameof(SubscriptionPlan.UserSubscriptions))
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
