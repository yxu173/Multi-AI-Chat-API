using System.Linq.Expressions;
using Domain.Aggregates.Chats;
using Domain.DomainErrors;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernal;

namespace Infrastructure.Repositories;

public class ChatSessionRepository : IChatSessionRepository
{
    private readonly ApplicationDbContext _context;

    public ChatSessionRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ChatSession> GetByIdAsync(Guid id)
    {
        return await _context.ChatSessions
            .AsNoTracking()
            .Include(c => c.Messages)
            .ThenInclude(m => m.FileAttachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<ChatSession> GetByIdWithMessagesAndModelAndProviderAsync(Guid id)
    {
        return await _context.ChatSessions
            .Include(c => c.Messages)
            .ThenInclude(m => m.FileAttachments)
            .Include(c => c.AiModel)
            .ThenInclude(m => m.AiProvider)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<ChatSession> GetChatWithModel(Guid chatId)
    {
        return await _context.ChatSessions
            .Include(c => c.AiModel)
            .FirstOrDefaultAsync(c => c.Id == chatId);
    }

    public async Task AddAsync(ChatSession chatSession, CancellationToken cancellationToken)
    {
        await _context.ChatSessions.AddAsync(chatSession, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ChatSession chatSession, CancellationToken cancellationToken)
    {
        _context.ChatSessions.Update(chatSession);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var chat = await _context.ChatSessions.FindAsync(id);
        if (chat == null)
        {
            return Result.Failure<bool>(ChatErrors.ChatNotFound);
        }

        _context.ChatSessions.Remove(chat);
        var result = await _context.SaveChangesAsync(cancellationToken);
        if (result == 0)
            return Result.Failure<bool>(ChatErrors.ChatNotFound);

        return Result.Success(true);
    }

    public async Task<IReadOnlyList<ChatSession>> GetAllChatsByUserId(Guid userId)
    {
        return await _context.ChatSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ChatSession>> GetChatSearch(Guid userId, string? searchTerm,
        bool includeMessages = false)
    {
        var query = _context.ChatSessions
            .AsNoTracking()
            .Where(c => c.UserId == userId);

        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(c =>
                c.Title.Contains(searchTerm) ||
                c.Messages.Any(m => m.Content.Contains(searchTerm)));
        }

        if (includeMessages)
        {
            query = query.Include(c => c.Messages
                    .OrderByDescending(m => m.CreatedAt))
                .ThenInclude(m => m.FileAttachments)
                .AsSplitQuery();
        }

        return await query.ToListAsync();
    }

    public async Task<int> BulkDeleteAsync(Guid userId, IEnumerable<Guid> chatIds, CancellationToken cancellationToken = default)
    {
        return await _context.ChatSessions
            .Where(c => c.UserId == userId && chatIds.Contains(c.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<ChatSession> Chats, int TotalCount)> GetRootChatsByUserIdAsync(Guid userId, int page, int pageSize)
    {
        var query = _context.ChatSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.FolderId == null);
        
        var totalCount = await query.CountAsync();
        var chats = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        
        return (chats, totalCount);
    }
}