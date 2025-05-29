using Domain.Aggregates.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class ProviderApiKeyConfiguration : IEntityTypeConfiguration<ProviderApiKey>
{
    public void Configure(EntityTypeBuilder<ProviderApiKey> builder)
    {
        builder.ToTable("ProviderApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.AiProviderId)
            .IsRequired();

        builder.Property(k => k.Secret)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(k => k.Label)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(k => k.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(k => k.MaxRequestsPerDay)
            .IsRequired();

        builder.Property(k => k.UsageCountToday)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.Property(k => k.CreatedByUserId)
            .IsRequired();

        builder.Property(k => k.LastUsedTimestamp);

        builder.HasOne(k => k.AiProvider)
            .WithMany()
            .HasForeignKey(k => k.AiProviderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
