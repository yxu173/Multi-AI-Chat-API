using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.Property(m => m.Content)
            .IsRequired();

        builder.Property(m => m.IsFromAi)
            .IsRequired();

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.ChatSessionId)
            .IsRequired();
        builder.HasIndex(m => m.CreatedAt);
        builder.HasOne(m => m.ChatSession)
            .WithMany(cs => cs.Messages)
            .HasForeignKey(m => m.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);


        builder.HasMany(m => m.FileAttachments)
            .WithOne()
            .HasForeignKey(fa => fa.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}