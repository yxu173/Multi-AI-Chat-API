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
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}