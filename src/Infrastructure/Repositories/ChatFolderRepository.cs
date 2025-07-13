using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Infrastructure.Repositories;

public class ChatFolderRepository : IChatFolderRepository
{
    private readonly ApplicationDbContext _context;

    public ChatFolderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ChatFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ChatFolders
            .Include(f => f.ChatSessions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<ChatFolder>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.ChatFolders
            .AsNoTracking()
            .Include(f => f.ChatSessions)
            .Where(f => f.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatFolder> AddAsync(ChatFolder folder, CancellationToken cancellationToken = default)
    {
        _context.ChatFolders.Add(folder);
        await _context.SaveChangesAsync(cancellationToken);
        return folder;
    }

    public async Task<ChatFolder> UpdateAsync(ChatFolder folder, CancellationToken cancellationToken = default)
    {
        _context.Entry(folder).State = EntityState.Modified;
        await _context.SaveChangesAsync(cancellationToken);
        return folder;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await GetByIdAsync(id, cancellationToken);
        if (folder != null)
        {
            _context.ChatFolders.Remove(folder);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}