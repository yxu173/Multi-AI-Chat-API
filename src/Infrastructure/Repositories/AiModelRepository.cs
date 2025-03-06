using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AiModelRepository : IAiModelRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AiModelRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AiModel?> GetByIdAsync(Guid id)
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IReadOnlyList<AiModel>> GetAllAsync()
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetByProviderIdAsync(Guid providerId)
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .Where(m => m.AiProviderId == providerId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetByModelTypeAsync(ModelType modelType)
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .Where(m => m.ModelType == modelType)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetEnabledAsync()
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .Where(m => m.IsEnabled && m.AiProvider.IsEnabled)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetEnabledByUserIdAsync(Guid userId)
    {
        return await _dbContext.UserAiModels
            .AsNoTracking()
            .Include(m => m.AiModel)
            .Where(x => x.UserId == userId && x.IsEnabled && x.AiModel.IsEnabled)
            .Select(x => x.AiModel)
            .ToListAsync();
    }

    public async Task AddAsync(AiModel aiModel)
    {
        await _dbContext.AiModels.AddAsync(aiModel);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(AiModel aiModel)
    {
        _dbContext.AiModels.Update(aiModel);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var model = await _dbContext.AiModels.FindAsync(id);
        if (model == null)
        {
            return false;
        }

        _dbContext.AiModels.Remove(model);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _dbContext.AiModels.AnyAsync(m => m.Id == id);
    }
}