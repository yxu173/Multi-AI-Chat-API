using Domain.Aggregates.Chats;
using Domain.Aggregates.Prompts;
using Domain.Aggregates.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Message> Messages { get; }
    DbSet<ChatSession> ChatSessions { get; }
    DbSet<FileAttachment> FileAttachments { get; }

    DbSet<PromptTemplate> PromptTemplates { get; }

    DbSet<AiProvider> AiProviders { get; }
    DbSet<AiModel> AiModels { get; }
    DbSet<ChatTokenUsage> ChatTokenUsages { get; }
    DbSet<UserApiKey> UserApiKeys { get; }

    DbSet<UserAiModel> UserAiModels { get; }

    DbSet<UserPlugin> UserPlugins { get; }
    DbSet<Plugin> Plugins { get; }

    DbSet<ChatFolder> ChatFolders { get; }

    DbSet<UserAiModelSettings> UserAiModelSettings { get; }
    DbSet<AiAgent> AiAgents { get; }

    DbSet<AiAgentPlugin> AiAgentPlugins { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}