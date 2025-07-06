using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class AiModelConfiguration : IEntityTypeConfiguration<AiModel>
{
    public void Configure(EntityTypeBuilder<AiModel> builder)
    {
        builder.ToTable("AiModels");

        builder.HasKey(am => am.Id);

        builder.Property(am => am.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(am => am.ModelType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(am => am.InputTokenPricePer1M)
            .IsRequired();

        builder.Property(am => am.OutputTokenPricePer1M)
            .IsRequired();

        builder.Property(am => am.ModelCode)
            .IsRequired()
            .HasMaxLength(100);

    //    builder.Property(am => am.MaxInputTokens);

        builder.Property(am => am.MaxOutputTokens);

        builder.Property(am => am.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        // Indexes for frequently queried columns
        builder.HasIndex(am => am.AiProviderId);
        builder.HasIndex(am => am.ModelType);
        builder.HasIndex(am => am.IsEnabled);
        builder.HasIndex(am => am.ModelCode);
        
        // Composite indexes for frequently used queries
        builder.HasIndex(am => new { am.AiProviderId, am.IsEnabled })
            .HasDatabaseName("IX_AiModels_AiProviderId_IsEnabled");
        
        builder.HasIndex(am => new { am.ModelType, am.IsEnabled })
            .HasDatabaseName("IX_AiModels_ModelType_IsEnabled");

        builder.HasOne(am => am.AiProvider)
            .WithMany(ap => ap.Models)
            .HasForeignKey(am => am.AiProviderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}