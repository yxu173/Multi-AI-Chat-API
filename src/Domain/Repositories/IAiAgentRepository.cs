using Domain.Aggregates.AiAgents;
using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IAiAgentRepository
{
    Task<AiAgent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<AiAgent>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AiAgent> AddAsync(AiAgent agent, CancellationToken cancellationToken = default);
    Task<AiAgent> UpdateAsync(AiAgent agent, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
