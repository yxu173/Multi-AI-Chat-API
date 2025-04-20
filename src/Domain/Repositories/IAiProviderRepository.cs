namespace Domain.Repositories;

public interface IAiProviderRepository
{
    Task<AiProvider?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<AiProvider>> GetAllAsync();
    Task<IReadOnlyList<AiProvider>> GetEnabledAsync();
    Task AddAsync(AiProvider aiProvider);
    Task UpdateAsync(AiProvider aiProvider);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);
    Task<bool> ExistsByNameAsync(string name);
}