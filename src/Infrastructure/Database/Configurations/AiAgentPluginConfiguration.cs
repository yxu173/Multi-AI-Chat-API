using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class AiAgentPluginConfiguration : IEntityTypeConfiguration<AiAgentPlugin>
{
    public void Configure(EntityTypeBuilder<AiAgentPlugin> builder)
    {
        builder.ToTable("AiAgentPlugins");

        builder.HasKey(ap => ap.Id);

        builder.Property(ap => ap.AiAgentId)
            .IsRequired();

        builder.Property(ap => ap.PluginId)
            .IsRequired();

        builder.Property(ap => ap.Order)
            .IsRequired();

        builder.Property(ap => ap.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Relationships
        builder.HasOne(ap => ap.AiAgent)
            .WithMany(a => a.AiAgentPlugins)
            .HasForeignKey(ap => ap.AiAgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ap => ap.Plugin)
            .WithMany()
            .HasForeignKey(ap => ap.PluginId)
            .OnDelete(DeleteBehavior.Cascade);

        // Create a unique index on the combination of AiAgentId and PluginId
        builder.HasIndex(ap => new { ap.AiAgentId, ap.PluginId }).IsUnique();
    }
} 