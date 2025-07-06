using Domain.Aggregates.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public sealed class UserAiModelConfiguration : IEntityTypeConfiguration<UserAiModel>
{
    public void Configure(EntityTypeBuilder<UserAiModel> builder)
    {
        builder.ToTable("UserAiModels");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.AiModelId)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .IsRequired();

        // Indexes for frequently queried columns
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.AiModelId);
        builder.HasIndex(x => x.IsEnabled);
        
        // Composite indexes for frequently used queries
        builder.HasIndex(x => new { x.UserId, x.AiModelId })
            .IsUnique()
            .HasDatabaseName("IX_UserAiModels_UserId_AiModelId");
        
        builder.HasIndex(x => new { x.UserId, x.IsEnabled })
            .HasDatabaseName("IX_UserAiModels_UserId_IsEnabled");

        builder.HasOne(x => x.User)
            .WithMany(x => x.UserAiModels)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AiModel)
            .WithMany(x => x.UserAiModels)
            .HasForeignKey(x => x.AiModelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}