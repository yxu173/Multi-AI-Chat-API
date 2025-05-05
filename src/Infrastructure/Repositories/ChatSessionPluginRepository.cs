using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ChatSessionPluginRepository : IChatSessionPluginRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ChatSessionPluginRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ChatSessionPlugin>> GetActivatedPluginsAsync(Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        var plugins = await _dbContext.ChatSessionPlugins
            .Where(csp => csp.ChatSessionId == chatSessionId && csp.IsActive)
            .Include(csp => csp.Plugin)
            .ToListAsync(cancellationToken);

        return plugins;
    }

    public async Task<IReadOnlyList<ChatSessionPlugin>> GetByChatSessionIdAsync(Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ChatSessionPlugins
            .Where(csp => csp.ChatSessionId == chatSessionId)
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatSessionPlugin?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.ChatSessionPlugins
            .FirstOrDefaultAsync(csp => csp.Id == id, cancellationToken);
    }

    public async Task AddAsync(ChatSessionPlugin chatSessionPlugin, CancellationToken cancellationToken)
    {
        await _dbContext.ChatSessionPlugins.AddAsync(chatSessionPlugin, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid chatPluginId, CancellationToken cancellationToken)
    {
        var chatSessionPlugin = await GetByIdAsync(chatPluginId, cancellationToken);
        _dbContext.ChatSessionPlugins.Remove(chatSessionPlugin);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}