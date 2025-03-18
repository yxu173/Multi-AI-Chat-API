using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AiAgentRepository : IAiAgentRepository
{
    private readonly ApplicationDbContext _context;

    public AiAgentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AiAgent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.AiAgents
            .Include(a => a.AiAgentPlugins)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<AiAgent>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.AiAgents
            .Include(a => a.AiAgentPlugins)
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiAgent> AddAsync(AiAgent agent, CancellationToken cancellationToken = default)
    {
        _context.AiAgents.Add(agent);
        await _context.SaveChangesAsync(cancellationToken);
        return agent;
    }

    public async Task<AiAgent> UpdateAsync(AiAgent agent, CancellationToken cancellationToken = default)
    {
        _context.Entry(agent).State = EntityState.Modified;

        foreach (var plugin in agent.AiAgentPlugins)
        {
            if (plugin.Id == Guid.Empty)
            {
                _context.Entry(plugin).State = EntityState.Added;
            }
            else
            {
                _context.Entry(plugin).State = EntityState.Modified;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return agent;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var agent = await GetByIdAsync(id, cancellationToken);
        if (agent != null)
        {
            _context.AiAgentPlugins.RemoveRange(agent.AiAgentPlugins);
            _context.AiAgents.Remove(agent);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}