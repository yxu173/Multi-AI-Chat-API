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
            .Include(a => a.AiModel)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<AiAgent>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.AiAgents
            .Include(a => a.AiAgentPlugins)
            .Include(a => a.AiModel)
            .Where(a => a.UserId == userId)
            .AsSplitQuery()
            .AsNoTracking()
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
        
        _context.Entry(agent).Property(a => a.Name).IsModified = true;
        _context.Entry(agent).Property(a => a.Description).IsModified = true;
        _context.Entry(agent).Property(a => a.AssignCustomModelParameters).IsModified = true;
        _context.Entry(agent).Property(a => a.ProfilePictureUrl).IsModified = true;
        _context.Entry(agent).Property(a => a.LastModifiedAt).IsModified = true;
        _context.Entry(agent).Property(a => a.AiModelId).IsModified = true;

        if (agent.AssignCustomModelParameters && agent.ModelParameter != null)
        {
            _context.Entry(agent).Reference(a => a.ModelParameter).IsModified = true;
        }
        else if (!agent.AssignCustomModelParameters)
        {           
        }

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
        var agent = await _context.AiAgents.FindAsync(new object[] { id }, cancellationToken);
        
        if (agent != null)
        {
            _context.AiAgents.Remove(agent);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}