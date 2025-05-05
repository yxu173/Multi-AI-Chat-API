using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public sealed class ChatFolderConfiguration : IEntityTypeConfiguration<ChatFolder>
{
    public void Configure(EntityTypeBuilder<ChatFolder> builder)
    {
        builder.HasMany(f => f.ChatSessions)
            .WithOne(c => c.Folder)
            .HasForeignKey(c => c.FolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}