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

            mp.Property(p => p.Temperature)
                .HasPrecision(3, 2)
                .HasColumnName("Temperature");


            mp.Property(p => p.MaxTokens)
                .HasColumnName("MaxTokens");
        });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}