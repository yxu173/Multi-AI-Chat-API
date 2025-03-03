using Domain.Aggregates.Chats;
using Domain.Enums;

namespace Domain.Repositories;

public interface IAiModelRepository
{
    Task<AiModel?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<AiModel>> GetAllAsync();
    Task<IReadOnlyList<AiModel>> GetByProviderIdAsync(Guid providerId);
    Task<IReadOnlyList<AiModel>> GetByModelTypeAsync(ModelType modelType);
    Task<IReadOnlyList<AiModel>> GetEnabledAsync();
    Task AddAsync(AiModel aiModel);
    Task UpdateAsync(AiModel aiModel);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);
}