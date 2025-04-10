using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("ChatSessions");

        builder.HasKey(cs => cs.Id);
        
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.CreatedAt);

        builder.Property(cs => cs.UserId)
            .IsRequired();

        builder.Property(cs => cs.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasMany(cs => cs.Messages)
            .WithOne(m => m.ChatSession)
            .HasForeignKey(m => m.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}