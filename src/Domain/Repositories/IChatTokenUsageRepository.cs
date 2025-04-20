using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatTokenUsageRepository
{
    Task<ChatTokenUsage> AddAsync(ChatTokenUsage tokenUsage, CancellationToken cancellationToken);
    Task<ChatTokenUsage?> GetByChatSessionIdAsync(Guid chatSessionId);
    Task UpdateAsync(ChatTokenUsage tokenUsage,CancellationToken cancellationToken);
    Task<int> GetTotalInputTokensForUserAsync(Guid userId);
    Task<int> GetTotalOutputTokensForUserAsync(Guid userId);
}