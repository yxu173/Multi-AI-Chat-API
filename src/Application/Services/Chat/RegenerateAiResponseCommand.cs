using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using FastEndpoints;

namespace Application.Services.Chat;

/// <summary>
/// Handles deleting previous AI response(s) for a user message and generating a new fresh response.
/// </summary>
public sealed class RegenerateAiResponseCommand : BaseAiChatCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly IMessageStreamer _messageStreamer;

    public RegenerateAiResponseCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        IMessageStreamer messageStreamer,
        IAiModelServiceFactory aiModelServiceFactory,
        IAiAgentRepository aiAgentRepository,
        IUserAiModelSettingsRepository userAiModelSettingsRepository)
        : base(aiModelServiceFactory, aiAgentRepository, userAiModelSettingsRepository)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _messageStreamer = messageStreamer ?? throw new ArgumentNullException(nameof(messageStreamer));
    }

    public async Task ExecuteAsync(
        Guid chatSessionId,
        Guid userId,
        Guid userMessageId,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);

        var userMessage = chatSession.Messages.FirstOrDefault(m => m.Id == userMessageId && m.UserId == userId && !m.IsFromAi);
        if (userMessage == null) throw new Exception("Original user message not found or access denied.");

        var aiMessageToDelete = chatSession.Messages
            .Where(m => m.IsFromAi && m.CreatedAt > userMessage.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefault();

        if (aiMessageToDelete != null)
        {
            chatSession.RemoveMessage(aiMessageToDelete);
            await _messageService.DeleteMessageAsync(aiMessageToDelete.Id, cancellationToken);
            await new MessageDeletedNotification(chatSessionId, aiMessageToDelete.Id).PublishAsync(cancellation: cancellationToken);
        }

        var newAiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        chatSession.AddMessage(newAiMessage);

        var (aiService, apiKey, aiAgent) = await base.PrepareForAiInteractionAsync(userId, chatSession, cancellationToken);
        var userSettings = await UserAiModelSettingsRepository.GetDefaultByUserIdAsync(userId, cancellationToken);

        var requestContext = new AiRequestContext(
            UserId: userId,
            ChatSession: chatSession,
            History: new List<MessageDto>(),
            AiAgent: aiAgent,
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel,
            RequestSpecificThinking: false,
            ImageSize: null,
            NumImages: null,
            OutputFormat: null,
            EnableSafetyChecker: null,
            SafetyTolerance: null);

        requestContext = requestContext with { History = HistoryBuilder.BuildHistory(requestContext, newAiMessage) };

        await _messageStreamer.StreamResponseAsync(requestContext, newAiMessage, aiService, cancellationToken);
    }
}
