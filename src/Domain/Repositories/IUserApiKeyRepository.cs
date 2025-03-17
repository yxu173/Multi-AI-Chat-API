using Domain.Aggregates.Users;

namespace Domain.Repositories;

public interface IUserApiKeyRepository
{
    Task<UserApiKey> GetByIdAsync(Guid id);
    Task<UserApiKey> GetByUserAndProviderAsync(Guid userId, Guid providerId);
    Task<IEnumerable<UserApiKey>> GetByUserIdAsync(Guid userId);
    Task AddAsync(UserApiKey userApiKey);
    Task UpdateAsync(UserApiKey userApiKey);
    Task<bool> DeleteAsync(Guid id);
}