using Domain.Aggregates.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class FileAttachmentConfiguration : IEntityTypeConfiguration<FileAttachment>
{
    public void Configure(EntityTypeBuilder<FileAttachment> builder)
    {
        builder.ToTable("FileAttachments");

        builder.HasKey(fa => fa.Id);

        builder.Property(fa => fa.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(fa => fa.FilePath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(fa => fa.MessageId)
            .IsRequired();
    }
}