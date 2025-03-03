using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class AiProviderConfiguration : IEntityTypeConfiguration<AiProvider>
{
    public void Configure(EntityTypeBuilder<AiProvider> builder)
    {
        builder.ToTable("AiProviders");

        builder.HasKey(ap => ap.Id);

        builder.Property(ap => ap.Name)
            .IsRequired()
            .HasMaxLength(200);
    }
} 