using Domain.Aggregates.Chats;
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

        builder.Property(am => am.ApiKey)
            .IsRequired();

        builder.Property(am => am.InputTokenPricePer1K)
            .IsRequired();

        builder.Property(am => am.OutputTokenPricePer1K)
            .IsRequired();
    }
} 