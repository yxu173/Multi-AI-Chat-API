using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatTokenUsageRepository
{
    Task<ChatTokenUsage> AddAsync(ChatTokenUsage tokenUsage);
    Task<ChatTokenUsage?> GetByChatSessionIdAsync(Guid chatSessionId);
    Task UpdateAsync(ChatTokenUsage tokenUsage);
    Task<int> GetTotalInputTokensForUserAsync(Guid userId);
    Task<int> GetTotalOutputTokensForUserAsync(Guid userId);
}