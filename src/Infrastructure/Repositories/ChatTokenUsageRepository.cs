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
        const int maxRetries = 3;
        int retryCount = 0;
        bool success = false;

        while (!success && retryCount < maxRetries)
        {
            try
            {
                if (_dbContext.Entry(tokenUsage).State == EntityState.Detached)
                {
                    var freshEntity = await _dbContext.ChatTokenUsages
                        .FirstOrDefaultAsync(t => t.Id == tokenUsage.Id);

                    if (freshEntity == null)
                    {
                        throw new InvalidOperationException($"ChatTokenUsage with ID {tokenUsage.Id} not found.");
                    }

                    freshEntity.UpdateTokenCountsAndCost(
                        tokenUsage.InputTokens - freshEntity.InputTokens,
                        tokenUsage.OutputTokens - freshEntity.OutputTokens,
                        tokenUsage.TotalCost - freshEntity.TotalCost);

                    _dbContext.Entry(freshEntity).State = EntityState.Modified;
                }
                else
                {
                    _dbContext.Entry(tokenUsage).State = EntityState.Modified;
                }

                await _dbContext.SaveChangesAsync();
                success = true;
            }
            catch (DbUpdateConcurrencyException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    throw;
                }

                _dbContext.ChangeTracker.Clear();

                tokenUsage = await _dbContext.ChatTokenUsages
                    .FirstOrDefaultAsync(t => t.Id == tokenUsage.Id);

                if (tokenUsage == null)
                {
                    throw new InvalidOperationException(
                        $"ChatTokenUsage with ID {tokenUsage.Id} not found during retry.");
                }
            }
        }
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