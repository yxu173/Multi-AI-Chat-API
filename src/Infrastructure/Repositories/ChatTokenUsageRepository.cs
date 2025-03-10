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
        _dbContext = dbContext;
    }

    public async Task<ChatTokenUsage> AddAsync(ChatTokenUsage tokenUsage)
    {
        var result = await _dbContext.ChatTokenUsages.AddAsync(tokenUsage);
        await _dbContext.SaveChangesAsync();
        return result.Entity;
    }

    public async Task<ChatTokenUsage?> GetByChatSessionIdAsync(Guid chatSessionId)
    {
        return await _dbContext.ChatTokenUsages
            .FirstOrDefaultAsync(t => t.ChatId == chatSessionId);
    }

    public async Task UpdateAsync(ChatTokenUsage tokenUsage)
    {
        _dbContext.Entry(tokenUsage).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<int> GetTotalInputTokensForUserAsync(Guid userId)
    {
        return await _dbContext.ChatTokenUsages
            .Where(t => t.ChatSession.UserId == userId)
            .SumAsync(t => t.InputTokens);
    }

    public async Task<int> GetTotalOutputTokensForUserAsync(Guid userId)
    {
        return await _dbContext.ChatTokenUsages
            .Where(t => t.ChatSession.UserId == userId)
            .SumAsync(t => t.OutputTokens);
    }
}