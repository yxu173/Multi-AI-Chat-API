using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IMessageRepository
{
    Task AddMessageAsync(Message message);
    Task<Message> GetMessageByIdAsync(Guid messageId);
    Task<IEnumerable<Message>> GetMessagesByChatSessionIdAsync(Guid chatSessionId);
    Task UpdateMessageAsync(Message message);
    Task DeleteMessageAsync(Message message);
}