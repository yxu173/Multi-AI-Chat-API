using Domain.Aggregates.Chats;
using Domain.Enums;

namespace Domain.Repositories;

public interface IChatRepository
{
    Task<Guid> CreateChatSessionAsync(ChatSession chatSession);
    Task<ChatSession> GetChatSessionByIdAsync(Guid chatSessionId);
    Task<IEnumerable<ChatSession>> GetChatSessionsByUserIdAsync(Guid userId);
}