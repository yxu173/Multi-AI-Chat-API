using Application.Abstractions.Data;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Prompts;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Database.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<User, Role, Guid>(options), IApplicationDbContext
{
    public DbSet<Message> Messages { get; set; }
    public DbSet<ChatSession> ChatSessions { get; set; }
    public DbSet<FileAttachment> FileAttachments { get; set; }

    public DbSet<PromptTemplate> PromptTemplates { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Ignore<Tag>();
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }


    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        int result = await base.SaveChangesAsync(cancellationToken);

        return result;
    }
}