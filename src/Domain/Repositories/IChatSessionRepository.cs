using System.Linq.Expressions;
using Domain.Aggregates.Chats;
using Domain.Enums;
using SharedKernel;

namespace Domain.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession> GetByIdAsync(Guid id);
   
   //Task<Result<T>> GetByIdAsync<T>(Guid id, Expression<Func<ChatSession, T>> selector); 
    Task AddAsync(ChatSession chatSession);
    Task UpdateAsync(ChatSession chatSession);
    Task<IReadOnlyList<ChatSession>> GetAllChatsByUserId(Guid userId);
}