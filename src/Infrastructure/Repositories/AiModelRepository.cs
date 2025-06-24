using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using Domain.Aggregates.Users;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AiModelRepository : IAiModelRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private const string CacheKeyPrefix = "aiModels";
    private readonly TimeSpan CacheExpiry = TimeSpan.FromDays(30);

    public AiModelRepository(ApplicationDbContext dbContext, ICacheService cacheService)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
    }

    public async Task<AiModel?> GetByIdAsync(Guid id)
    {
        string cacheKey = $"{CacheKeyPrefix}:{id}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiModels
                .Include(m => m.AiProvider)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id),
            CacheExpiry);
    }

    public async Task<string?> GetModelNameById(Guid id)
    {
        string cacheKey = $"{CacheKeyPrefix}:name:{id}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiModels
                .Where(m => m.Id == id)
                .Select(m => m.Name)
                .FirstOrDefaultAsync(),
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiModel>> GetAllAsync()
    {
        string cacheKey = $"{CacheKeyPrefix}:all";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiModels
                .Include(m => m.AiProvider)
                .AsNoTracking()
                .ToListAsync(),
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiModel>> GetByProviderIdAsync(Guid providerId)
    {
        string cacheKey = $"{CacheKeyPrefix}:provider:{providerId}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiModels
                .Include(m => m.AiProvider)
                .Where(m => m.AiProviderId == providerId)
                .AsNoTracking()
                .ToListAsync(),
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiModel>> GetByModelTypeAsync(ModelType modelType)
    {
        string cacheKey = $"{CacheKeyPrefix}:type:{modelType}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiModels
                .Include(m => m.AiProvider)
                .Where(m => m.ModelType == modelType)
                .AsNoTracking()
                .ToListAsync(),
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiModel>> GetEnabledAsync()
    {
        string cacheKey = $"{CacheKeyPrefix}:enabled";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiModels
                .Include(m => m.AiProvider)
                .Where(m => m.IsEnabled && m.AiProvider.IsEnabled)
                .AsNoTracking()
                .ToListAsync(),
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiModel>> GetEnabledByUserIdAsync(Guid userId)
    {
        string cacheKey = $"{CacheKeyPrefix}:enabled:user:{userId}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var userModels = await _dbContext.UserAiModels
                    .Where(um => um.UserId == userId)
                    .ToListAsync();
                var hiddenModelIds = userModels
                    .Where(um => !um.IsEnabled)
                    .Select(um => um.AiModelId)
                    .ToHashSet();
                return await _dbContext.AiModels
                    .Include(m => m.AiProvider)
                    .Where(m => m.IsEnabled
                                && m.AiProvider.IsEnabled
                                && !hiddenModelIds.Contains(m.Id))
                    .AsNoTracking()
                    .ToListAsync();
            },
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiModel>> GetUserAiModelsAsync(Guid userId)
    {
        string cacheKey = $"{CacheKeyPrefix}:user:{userId}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () =>
            {
                var allModels = await _dbContext.AiModels
                    .Where(m => m.IsEnabled)
                    .ToListAsync();
                var disabledModelIds = await _dbContext.UserAiModels
                    .Where(x => x.UserId == userId && !x.IsEnabled)
                    .Select(x => x.AiModelId)
                    .ToListAsync();
                return allModels.Where(m => !disabledModelIds.Contains(m.Id)).ToList();
            },
            CacheExpiry);
    }

    public async Task AddAsync(AiModel aiModel)
    {
        await _dbContext.AiModels.AddAsync(aiModel);
        await _dbContext.SaveChangesAsync();
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
    }

    public async Task UpdateAsync(AiModel aiModel)
    {
        _dbContext.Entry(aiModel).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var aiModel = await _dbContext.AiModels.FindAsync(id);
        if (aiModel != null)
        {
            _dbContext.AiModels.Remove(aiModel);
            await _dbContext.SaveChangesAsync();
            await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
            return true;
        }

        return false;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        string cacheKey = $"{CacheKeyPrefix}:exists:{id}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiModels.AnyAsync(m => m.Id == id),
            CacheExpiry);
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
            await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
        }

        return userAiModel;
    }

    public async Task AddUserAiModelAsync(UserAiModel userAiModel)
    {
        await _dbContext.UserAiModels.AddAsync(userAiModel);
        await _dbContext.SaveChangesAsync();
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
    }

    public async Task UpdateUserAiModelAsync(UserAiModel userAiModel)
    {
        _dbContext.UserAiModels.Update(userAiModel);
        await _dbContext.SaveChangesAsync();
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
    }
}