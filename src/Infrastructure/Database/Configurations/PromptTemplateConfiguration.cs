using Domain.Aggregates.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> builder)
    {
        builder.ToTable("PromptTemplates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired();

        builder.Property(x => x.Content)
            .IsRequired();

        builder.OwnsMany(x => x.Tags, tagsBuilder =>
        {
            tagsBuilder.ToTable("PromptTemplateTags");

            tagsBuilder.WithOwner().HasForeignKey("PromptTemplateId");

            tagsBuilder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(50);

            tagsBuilder.HasKey("PromptTemplateId", "Name");

            tagsBuilder.HasIndex("Name");
        });
    }
}