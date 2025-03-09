using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
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
        var enabledModels = await _dbContext.AiModels
            .Where(m => m.IsEnabled)
            .ToListAsync();

        var disabledModelIds = await _dbContext.UserAiModels
            .Where(x => x.UserId == userId && !x.IsEnabled)
            .Select(x => x.AiModelId)
            .ToListAsync();

        var userEnabledModels = enabledModels
            .Where(m => !disabledModelIds.Contains(m.Id))
            .ToList();

        return userEnabledModels;
    }

    public async Task<IReadOnlyList<AiModel>> GetUserAiModelsAsync(Guid userId)
    {
        var allModels = await _dbContext.AiModels
            .Where(m => m.IsEnabled)
            .ToListAsync();

        var disabledModelIds = await _dbContext.UserAiModels
            .Where(x => x.UserId == userId && !x.IsEnabled)
            .Select(x => x.AiModelId)
            .ToListAsync();

        return allModels.Where(m => !disabledModelIds.Contains(m.Id)).ToList();
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

    public async Task<UserAiModel> GetUserAiModelAsync(Guid userId, Guid aiModelId)
    {
        return await _dbContext.UserAiModels
            .FirstOrDefaultAsync(x => x.UserId == userId && x.AiModelId == aiModelId);
    }

    public async Task AddUserAiModelAsync(UserAiModel userAiModel)
    {
        await _dbContext.UserAiModels.AddAsync(userAiModel);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateUserAiModelAsync(UserAiModel userAiModel)
    {
        _dbContext.UserAiModels.Update(userAiModel);
        await _dbContext.SaveChangesAsync();
    }
}