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

        builder.HasOne(am => am.AiProvider)
            .WithMany(ap => ap.Models)
            .HasForeignKey(am => am.AiProviderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}