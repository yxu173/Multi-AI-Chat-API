using Domain.Aggregates.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public sealed class UserAiModelSettingConfiguration : IEntityTypeConfiguration<UserAiModelSettings>
{
    public void Configure(EntityTypeBuilder<UserAiModelSettings> builder)
    {
        builder.ToTable("UserAiModelSettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

         builder.OwnsOne(a => a.ModelParameters, mp =>
        {
            mp.Property(p => p.SystemInstructions)
                .HasMaxLength(1000)
                .HasColumnName("SystemInstructions");
            
            mp.Property(p => p.DefaultModel)
                .IsRequired()
                .HasColumnName("DefaultModel");
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

            mp.Property(p => p.StopSequences)
                .HasConversion(
                    v => v != null ? string.Join(',', v) : null,
                    v => v != null ? v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() : null
                )
                .HasColumnName("StopSequences")
                .HasColumnType("text")
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>?>(
                    (c1, c2) => c1 == null && c2 == null || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                    c => c == null ? 0 : c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c == null ? null : c.ToList()
                ));

   

            mp.Property(p => p.PromptCaching)
                .HasColumnName("PromptCaching");

            mp.Property(p => p.SafetySettings)
                .HasMaxLength(1000)
                .HasColumnName("SafetySettings");
        });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
    }
}