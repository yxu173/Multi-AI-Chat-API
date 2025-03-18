using Domain.Aggregates.Users;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserAiModelSettingsRepository : IUserAiModelSettingsRepository
{
    private readonly ApplicationDbContext _context;

    public UserAiModelSettingsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserAiModelSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.UserAiModelSettings
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<UserAiModelSettings?> GetByUserAndModelIdAsync(Guid userId, Guid modelId, CancellationToken cancellationToken = default)
    {
        return await _context.UserAiModelSettings
            .FirstOrDefaultAsync(s => s.UserId == userId && s.AiModelId == modelId, cancellationToken);
    }

    public async Task<IEnumerable<UserAiModelSettings>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserAiModelSettings
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserAiModelSettings?> GetDefaultByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserAiModelSettings
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsDefault, cancellationToken);
    }

    public async Task<UserAiModelSettings> AddAsync(UserAiModelSettings settings, CancellationToken cancellationToken = default)
    {
        _context.UserAiModelSettings.Add(settings);
        await _context.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task<UserAiModelSettings> UpdateAsync(UserAiModelSettings settings, CancellationToken cancellationToken = default)
    {
        _context.Entry(settings).State = EntityState.Modified;
        await _context.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var settings = await GetByIdAsync(id, cancellationToken);
        if (settings != null)
        {
            _context.UserAiModelSettings.Remove(settings);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
