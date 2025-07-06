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
        
        // Single column indexes
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.FolderId);
        
        // Composite indexes for frequently used queries
        builder.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("IX_ChatSessions_UserId_CreatedAt");
        
        builder.HasIndex(x => new { x.UserId, x.FolderId })
            .HasDatabaseName("IX_ChatSessions_UserId_FolderId");
        
        builder.HasIndex(x => new { x.UserId, x.ChatType })
            .HasDatabaseName("IX_ChatSessions_UserId_ChatType");

        builder.Property(cs => cs.UserId)
            .IsRequired();

        builder.Property(cs => cs.Title)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(cs => cs.ChatType)
            .IsRequired()
            .HasConversion<string>();

        builder.HasMany(cs => cs.Messages)
            .WithOne(m => m.ChatSession)
            .HasForeignKey(m => m.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}