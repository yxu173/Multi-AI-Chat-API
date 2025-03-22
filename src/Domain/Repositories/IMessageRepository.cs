using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IMessageRepository
{
    Task AddAsync(Message message, CancellationToken cancellationToken);
    Task UpdateAsync(Message message, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}