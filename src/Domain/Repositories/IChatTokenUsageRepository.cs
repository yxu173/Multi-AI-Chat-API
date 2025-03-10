using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatTokenUsageRepository
{
    Task AddAsync(ChatTokenUsage chatTokenUsage);
    Task<ChatTokenUsage?> GetByIdAsync(Guid chatId);
    Task UpdateAsync(ChatTokenUsage chatTokenUsage);
}