using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.Enums;

namespace Domain.Repositories;

public interface IAiModelRepository
{
    Task<AiModel?> GetByIdAsync(Guid id);
    Task<string?> GetModelNameById(Guid id);
    Task<IReadOnlyList<AiModel>> GetAllAsync();
    Task<IReadOnlyList<AiModel>> GetByProviderIdAsync(Guid providerId);
    Task<IReadOnlyList<AiModel>> GetByModelTypeAsync(ModelType modelType);
    Task<IReadOnlyList<AiModel>> GetEnabledAsync();
    Task<IReadOnlyList<AiModel>> GetEnabledByUserIdAsync(Guid userId);
    Task<IReadOnlyList<AiModel>> GetUserAiModelsAsync(Guid userId);

    Task AddAsync(AiModel aiModel);
    Task UpdateAsync(AiModel aiModel);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);

    Task<UserAiModel> GetUserAiModelAsync(Guid userId, Guid aiModelId);
    Task AddUserAiModelAsync(UserAiModel userAiModel);
    Task UpdateUserAiModelAsync(UserAiModel userAiModel);
}