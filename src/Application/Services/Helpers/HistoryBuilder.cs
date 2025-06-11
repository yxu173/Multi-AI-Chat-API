using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Services.Helpers;

internal static class HistoryBuilder
{
    public static List<MessageDto> BuildHistory(
        ChatSession chatSession,
        AiAgent? aiAgent,
        UserAiModelSettings? userSettings,
        MessageDto currentAiMessagePlaceholder)
    {
        ArgumentNullException.ThrowIfNull(chatSession);
        ArgumentNullException.ThrowIfNull(currentAiMessagePlaceholder);

        var contextLimit = 0;
        if (aiAgent?.AssignCustomModelParameters == true && aiAgent.ModelParameter != null)
        {
            contextLimit = aiAgent.ModelParameter.ContextLimit;
        }
        else if (userSettings != null)
        {
            contextLimit = userSettings.ModelParameters.ContextLimit;
        }

        var messagesQuery = chatSession.Messages
            .Where(m => m.Id != currentAiMessagePlaceholder.MessageId)
            .OrderBy(m => m.CreatedAt);

        IEnumerable<Message> limitedMessages = contextLimit > 0
            ? messagesQuery.TakeLast(contextLimit)
            : messagesQuery;

        return limitedMessages
            .Select(m => MessageDto.FromEntity(m, overrideThinkingContent: null))
            .ToList();
    }
}