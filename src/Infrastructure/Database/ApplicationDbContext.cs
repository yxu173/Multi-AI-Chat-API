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

    public DbSet<AiProvider> AiProviders { get; set; }

    public DbSet<AiModel> AiModels { get; set; }
    public DbSet<ChatTokenUsage> ChatTokenUsages { get; set; }
    public DbSet<UserApiKey> UserApiKeys { get; set; }
    public DbSet<UserAiModel> UserAiModels { get; set; }
    public DbSet<UserPlugin> UserPlugins { get; set; }
    public DbSet<Plugin> Plugins { get; set; }
    public DbSet<ChatSessionPlugin> ChatSessionPlugins { get; set; }
    public DbSet<ChatFolder> ChatFolders { get; set; }
    public DbSet<UserAiModelSettings> UserAiModelSettings { get; set; }
    public DbSet<AiAgent> AiAgents { get; set; }
    public DbSet<AiAgentPlugin> AiAgentPlugins { get; set; }


    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Ignore<Tag>();
        builder.Ignore<ModelParameters>();
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }


    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        int result = await base.SaveChangesAsync(cancellationToken);

        return result;
    }
}