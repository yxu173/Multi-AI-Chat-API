 using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatTokenUsageRepository
{
    Task<ChatTokenUsage> GetByIdAsync(Guid id);
    Task<ChatTokenUsage> GetByMessageIdAsync(Guid messageId);
    Task<IEnumerable<ChatTokenUsage>> GetByChatSessionIdAsync(Guid chatSessionId);
    Task<IEnumerable<ChatTokenUsage>> GetByUserIdAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null);
    Task AddAsync(ChatTokenUsage chatTokenUsage);
}