using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly ApplicationDbContext _context;

    public MessageRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Messages
            .AsNoTracking()
            .Include(m => m.FileAttachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Message?> GetByIdWithFileAttachmentsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Messages
            .AsNoTracking()
            .Include(m => m.FileAttachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(Message message, CancellationToken cancellationToken)
    {
        await _context.Messages.AddAsync(message, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Message message, CancellationToken cancellationToken)
    {
        _context.Messages.Update(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var message = await _context.Messages.FindAsync(id);
        if (message is not null)
        {
            _context.Messages.Remove(message);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task BulkDeleteAsync(Guid userId, IEnumerable<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        await _context.Messages
            .Where(m => messageIds.Contains(m.Id) && m.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<Message?> GetLatestAiMessageForChatAsync(Guid chatSessionId, CancellationToken? cancellationToken = null)
    {
        var token = cancellationToken ?? CancellationToken.None;
        
        return await _context.Messages
            .Where(m => m.ChatSessionId == chatSessionId && m.IsFromAi && m.Status == Domain.Enums.MessageStatus.Streaming)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(token);
    }
}