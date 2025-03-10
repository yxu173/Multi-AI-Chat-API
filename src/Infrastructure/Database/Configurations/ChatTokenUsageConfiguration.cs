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

        builder.HasOne(tu => tu.ChatSession)
            .WithOne()
            .HasForeignKey<ChatTokenUsage>(tu => tu.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}