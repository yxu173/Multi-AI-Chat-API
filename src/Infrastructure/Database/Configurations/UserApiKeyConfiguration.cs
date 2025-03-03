 using Domain.Aggregates.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class UserApiKeyConfiguration : IEntityTypeConfiguration<UserApiKey>
{
    public void Configure(EntityTypeBuilder<UserApiKey> builder)
    {
        builder.ToTable("UserApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.UserId)
            .IsRequired();

        builder.Property(k => k.AiProviderId)
            .IsRequired();

        builder.Property(k => k.ApiKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.Property(k => k.LastUsed);

        builder.HasOne(k => k.User)
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(k => k.AiProvider)
            .WithMany()
            .HasForeignKey(k => k.AiProviderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}