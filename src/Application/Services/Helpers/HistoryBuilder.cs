using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using Domain.Aggregates.AiAgents;

namespace Application.Services.Helpers;

internal static class HistoryBuilder
{
    private const int RecentMessagesToKeep = 6;
    
    public static List<MessageDto> BuildHistory(
        ChatSession chatSession,
        MessageDto currentAiMessagePlaceholder)
    {
        ArgumentNullException.ThrowIfNull(chatSession);
        ArgumentNullException.ThrowIfNull(currentAiMessagePlaceholder);

        var allMessages = chatSession.Messages
            .Where(m => m.Id != currentAiMessagePlaceholder.MessageId)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        
       
            var summaryMessage = new MessageDto(
                $"This is a summary of the conversation so far: {chatSession.HistorySummary}",
                true,
                Guid.NewGuid() 
            );

            var recentMessages = allMessages
                .TakeLast(RecentMessagesToKeep)
                .Select(m => MessageDto.FromEntity(m));
            
            var condensedHistory = new List<MessageDto> { summaryMessage };
            condensedHistory.AddRange(recentMessages);
            return condensedHistory;
    }
    
    public static List<MessageDto> BuildHistory(List<MessageDto> messages)
    {
        return new List<MessageDto>(messages);
    }
}