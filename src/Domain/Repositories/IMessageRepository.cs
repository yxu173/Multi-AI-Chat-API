using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IMessageRepository
{
    Task AddAsync(Message message);
    Task UpdateAsync(Message message);
}