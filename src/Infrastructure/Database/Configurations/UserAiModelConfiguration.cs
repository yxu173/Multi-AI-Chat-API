using Domain.Aggregates.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configurations;

public sealed class UserAiModelConfiguration : IEntityTypeConfiguration<UserAiModel>
{
    public void Configure(EntityTypeBuilder<UserAiModel> builder)
    {
        builder.ToTable("UserAiModels");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.AiModelId)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany(x => x.UserAiModels)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AiModel)
            .WithMany(x => x.UserAiModels)
            .HasForeignKey(x => x.AiModelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}