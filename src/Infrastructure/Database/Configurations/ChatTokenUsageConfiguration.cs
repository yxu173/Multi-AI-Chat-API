using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class ChatTokenUsageConfiguration : IEntityTypeConfiguration<ChatTokenUsage>
{
    public void Configure(EntityTypeBuilder<ChatTokenUsage> builder)
    {
        builder.ToTable("ChatTokenUsages");

        builder.HasKey(tu => tu.Id);

        builder.Property(tu => tu.ChatId)
            .IsRequired();

        builder.Property(tu => tu.InputTokens)
            .IsRequired();

        builder.Property(tu => tu.OutputTokens)
            .IsRequired();

        builder.Property(tu => tu.TotalCost)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(tu => tu.CreatedAt)
            .IsRequired();

        // Indexes for frequently queried columns
        builder.HasIndex(tu => tu.ChatId);
        builder.HasIndex(tu => tu.CreatedAt);
        builder.HasIndex(tu => tu.TotalCost);
        
        // Composite indexes for frequently used queries
        builder.HasIndex(tu => new { tu.ChatId, tu.CreatedAt })
            .HasDatabaseName("IX_ChatTokenUsages_ChatId_CreatedAt");

        builder.HasOne(tu => tu.ChatSession)
            .WithOne()
            .HasForeignKey<ChatTokenUsage>(tu => tu.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}