using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ChatTokenUsageRepository : IChatTokenUsageRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ChatTokenUsageRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<ChatTokenUsage?> GetByIdAsync(Guid id)
    {
        return await _dbContext.ChatTokenUsages
            .AsNoTracking()
            .Include(u => u.Message)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<ChatTokenUsage?> GetByMessageIdAsync(Guid messageId)
    {
        return await _dbContext.ChatTokenUsages
            .AsNoTracking()
            .Include(u => u.Message)
            .FirstOrDefaultAsync(u => u.MessageId == messageId);
    }

    public async Task<IEnumerable<ChatTokenUsage>> GetByChatSessionIdAsync(Guid chatSessionId)
    {
        return await _dbContext.ChatTokenUsages
            .AsNoTracking()
            .Include(u => u.Message)
            .Where(u => u.Message.ChatSessionId == chatSessionId)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<ChatTokenUsage>> GetByUserIdAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _dbContext.ChatTokenUsages
            .AsNoTracking()
            .Include(u => u.Message)
            .Where(u => u.Message.UserId == userId);
        
        if (startDate.HasValue)
        {
            query = query.Where(u => u.CreatedAt >= startDate.Value);
        }
        
        if (endDate.HasValue)
        {
            query = query.Where(u => u.CreatedAt <= endDate.Value);
        }
        
        return await query
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
    }

    
  
    public async Task AddAsync(ChatTokenUsage chatTokenUsage)
    {
        if (chatTokenUsage == null)
        {
            throw new ArgumentNullException(nameof(chatTokenUsage));
        }

        await _dbContext.ChatTokenUsages.AddAsync(chatTokenUsage);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _dbContext.ChatTokenUsages
            .AsNoTracking()
            .AnyAsync(u => u.Id == id);
    }

    public async Task<bool> ExistsByMessageIdAsync(Guid messageId)
    {
        return await _dbContext.ChatTokenUsages
            .AsNoTracking()
            .AnyAsync(u => u.MessageId == messageId);
    }
}