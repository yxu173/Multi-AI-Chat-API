using Domain.Aggregates.Users;

namespace Domain.Repositories;

public interface IUserAiModelSettingsRepository
{
    Task<UserAiModelSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserAiModelSettings?> GetByUserAndModelIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserAiModelSettings>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserAiModelSettings?> GetDefaultByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserAiModelSettings> AddAsync(UserAiModelSettings settings, CancellationToken cancellationToken = default);
    Task<UserAiModelSettings> UpdateAsync(UserAiModelSettings settings, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
