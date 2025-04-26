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

    public async Task<List<ChatSessionPlugin>> GetActivatedPluginsAsync(Guid chatSessionId, CancellationToken cancellationToken)
    {
        var plugins = await _dbContext.ChatSessionPlugins
            .Where(csp => csp.ChatSessionId == chatSessionId && csp.IsActive)
            .Include(csp => csp.Plugin)
            .ToListAsync(cancellationToken);
            
        return plugins;
    }

    public async Task AddAsync(ChatSessionPlugin chatSessionPlugin, CancellationToken cancellationToken)
    {
        await _dbContext.ChatSessionPlugins.AddAsync(chatSessionPlugin, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}