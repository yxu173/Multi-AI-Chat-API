using Application.Services.AI;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;

namespace Application.Services.Helpers;

internal static class HistoryBuilder
{
    public static List<MessageDto> BuildHistory(
        AiRequestContext context,
        Message currentAiMessagePlaceholder)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentAiMessagePlaceholder);

        var chatSession = context.ChatSession;
        var aiAgent = context.AiAgent;
        var userSettings = context.UserSettings;

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
            .Where(m => m.Id != currentAiMessagePlaceholder.Id)
            .OrderBy(m => m.CreatedAt);

        IEnumerable<Message> limitedMessages = contextLimit > 0
            ? messagesQuery.TakeLast(contextLimit)
            : messagesQuery;

        return limitedMessages
            .Select(m => MessageDto.FromEntity(m, overrideThinkingContent: null))
            .ToList();
    }
}