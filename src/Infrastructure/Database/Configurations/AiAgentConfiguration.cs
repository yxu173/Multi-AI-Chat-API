using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class AiAgentConfiguration : IEntityTypeConfiguration<AiAgent>
{
    public void Configure(EntityTypeBuilder<AiAgent> builder)
    {
        builder.ToTable("AiAgents");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserId)
            .IsRequired();

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.SystemInstructions)
            .HasMaxLength(4000);

        builder.Property(a => a.AiModelId)
            .IsRequired();

        builder.Property(a => a.IconUrl)
            .HasMaxLength(500);

        builder.Property(a => a.Categories)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            )
            .HasColumnType("text");

        builder.Property(a => a.AssignCustomModelParameters)
            .IsRequired()
            .HasDefaultValue(false);

        // Configure ModelParameter as an owned entity
        builder.OwnsOne(a => a.ModelParameter, mp =>
        {
            mp.Property(p => p.ContextLimit)
                .HasMaxLength(100)
                .HasColumnName("ContextLimit");

            mp.Property(p => p.Temperature)
                .HasPrecision(3, 2)
                .HasColumnName("Temperature");

            mp.Property(p => p.PresencePenalty)
                .HasPrecision(3, 2)
                .HasColumnName("PresencePenalty");

            mp.Property(p => p.FrequencyPenalty)
                .HasPrecision(3, 2)
                .HasColumnName("FrequencyPenalty");

            mp.Property(p => p.TopP)
                .HasPrecision(3, 2)
                .HasColumnName("TopP");

            mp.Property(p => p.TopK)
                .HasColumnName("TopK");

            mp.Property(p => p.MaxTokens)
                .HasColumnName("MaxTokens");

            mp.Property(p => p.EnableThinking)
                .HasColumnName("EnableThinking");

            mp.Property(p => p.StopSequences)
                .HasConversion(
                    v => v != null ? string.Join(',', v) : null,
                    v => v != null ? v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() : null
                )
                .HasColumnName("StopSequences")
                .HasColumnType("text");

            mp.Property(p => p.ReasoningEffort)
                .HasColumnName("ReasoningEffort");

            mp.Property(p => p.PromptCaching)
                .HasColumnName("PromptCaching");

            mp.Property(p => p.SafetySettings)
                .HasMaxLength(1000)
                .HasColumnName("SafetySettings");
        });

        builder.Property(a => a.ProfilePictureUrl)
            .HasMaxLength(500);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.LastModifiedAt);

        // Relationships
        builder.HasOne(a => a.AiModel)
            .WithMany()
            .HasForeignKey(a => a.AiModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.AiAgentPlugins)
            .WithOne(ap => ap.AiAgent)
            .HasForeignKey(ap => ap.AiAgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}