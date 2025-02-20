using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Message> Messages { get; }
    DbSet<ChatSession> ChatSessions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}