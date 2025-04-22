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
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<string?> GetModelNameById(Guid id)
    {
        return await _dbContext.AiModels
            .Where(m => m.Id == id)
            .Select(m => m.Name)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetAllAsync()
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetByProviderIdAsync(Guid providerId)
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .Where(m => m.AiProviderId == providerId)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetByModelTypeAsync(ModelType modelType)
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .Where(m => m.ModelType == modelType)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetEnabledAsync()
    {
        return await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .Where(m => m.IsEnabled && m.AiProvider.IsEnabled)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiModel>> GetEnabledByUserIdAsync(Guid userId)
    {
        // First get the user-specific model preferences
        var userModels = await _dbContext.UserAiModels
            .Where(um => um.UserId == userId)
            .ToListAsync();

        // Get the IDs of models that should be hidden
        var hiddenModelIds = userModels
            .Where(um => !um.IsEnabled)
            .Select(um => um.AiModelId)
            .ToHashSet();

        // Get all enabled models from the system that are not explicitly hidden for this user
        var enabledModels = await _dbContext.AiModels
            .Include(m => m.AiProvider)
            .Where(m => m.IsEnabled
                        && m.AiProvider.IsEnabled
                        && !hiddenModelIds.Contains(m.Id))
            .AsNoTracking()
            .ToListAsync();

        return enabledModels;
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
        _dbContext.Entry(aiModel).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var aiModel = await _dbContext.AiModels.FindAsync(id);
        if (aiModel != null)
        {
            _dbContext.AiModels.Remove(aiModel);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _dbContext.AiModels.AnyAsync(m => m.Id == id);
    }

    public async Task<UserAiModel> GetUserAiModelAsync(Guid userId, Guid modelId)
    {
        var userAiModel = await _dbContext.UserAiModels
            .FirstOrDefaultAsync(u => u.UserId == userId && u.AiModelId == modelId);

        if (userAiModel == null)
        {
            var aiModel = await GetByIdAsync(modelId);
            if (aiModel == null)
            {
                throw new ArgumentException($"AI Model with ID {modelId} not found.");
            }

            userAiModel = UserAiModel.Create(userId, modelId);
            _dbContext.UserAiModels.Add(userAiModel);
            await _dbContext.SaveChangesAsync();
        }

        return userAiModel;
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