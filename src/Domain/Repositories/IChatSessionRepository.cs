using Domain.Aggregates.Chats;
using Domain.Enums;

namespace Domain.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession> GetByIdAsync(Guid id);
    Task AddAsync(ChatSession chatSession);
    Task UpdateAsync(ChatSession chatSession);
}