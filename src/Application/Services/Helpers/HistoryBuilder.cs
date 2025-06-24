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
        AiAgent? aiAgent,
        UserAiModelSettings? userSettings,
        MessageDto currentAiMessagePlaceholder)
    {
        ArgumentNullException.ThrowIfNull(chatSession);
        ArgumentNullException.ThrowIfNull(currentAiMessagePlaceholder);

        var allMessages = chatSession.Messages
            .Where(m => m.Id != currentAiMessagePlaceholder.MessageId)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        
        // If a recent summary exists, use it to condense the history
        if (!string.IsNullOrWhiteSpace(chatSession.HistorySummary) &&
            chatSession.LastSummarizedAt.HasValue &&
            allMessages.Count > RecentMessagesToKeep)
        {
            var summaryMessage = new MessageDto(
                $"This is a summary of the conversation so far: {chatSession.HistorySummary}",
                true, // The summary acts as a system/AI message
                Guid.NewGuid() 
            );

            var recentMessages = allMessages
                .TakeLast(RecentMessagesToKeep)
                .Select(m => MessageDto.FromEntity(m));
            
            var condensedHistory = new List<MessageDto> { summaryMessage };
            condensedHistory.AddRange(recentMessages);
            return condensedHistory;
        }

        // Default behavior: use context limit if no summary is applicable
        var contextLimit = 0;
        if (aiAgent?.AssignCustomModelParameters == true && aiAgent.ModelParameter != null)
        {
            contextLimit = aiAgent.ModelParameter.ContextLimit;
        }
        else if (userSettings != null)
        {
            contextLimit = userSettings.ModelParameters.ContextLimit;
        }

        IEnumerable<Message> limitedMessages = contextLimit > 0
            ? allMessages.TakeLast(contextLimit)
            : allMessages;

        return limitedMessages
            .Select(m => MessageDto.FromEntity(m, overrideThinkingContent: null))
            .ToList();
    }
    
    public static List<MessageDto> BuildHistory(List<MessageDto> messages)
    {
        // When history is provided directly, we assume it's already processed and correct.
        // We just need to ensure it's a new list instance.
        return new List<MessageDto>(messages);
    }
}