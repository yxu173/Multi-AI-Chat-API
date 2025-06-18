using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class SharedChatRepository : ISharedChatRepository
{
    private readonly ApplicationDbContext _context;

    public SharedChatRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<SharedChat?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.SharedChats
            .Include(s => s.Chat)
            .ThenInclude(c => c.Messages)
            .FirstOrDefaultAsync(s => 
                s.ShareToken == token && 
                s.IsActive && 
                (!s.ExpiresAt.HasValue || s.ExpiresAt > now), 
                cancellationToken);
    }

    public async Task<SharedChat?> GetByChatIdAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.SharedChats
            .Include(s => s.Chat)
            .ThenInclude(c => c.Messages)
            .FirstOrDefaultAsync(s => 
                s.ChatId == chatId && 
                s.IsActive && 
                (!s.ExpiresAt.HasValue || s.ExpiresAt > now), 
                cancellationToken);
    }

    public async Task<IReadOnlyList<SharedChat>> GetSharedByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.SharedChats
            .Include(s => s.Chat)
            .Where(s => 
                s.OwnerId == userId && 
                s.IsActive && 
                (!s.ExpiresAt.HasValue || s.ExpiresAt > now))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default)
    {
        await _context.SharedChats.AddAsync(sharedChat, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SharedChat sharedChat, CancellationToken cancellationToken = default)
    {
        _context.SharedChats.Update(sharedChat);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sharedChat = await _context.SharedChats.FindAsync(new object[] { id }, cancellationToken);
        if (sharedChat == null)
            return false;

        _context.SharedChats.Remove(sharedChat);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
} 