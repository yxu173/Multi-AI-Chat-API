using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.Helpers;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using FastEndpoints;

namespace Application.Services.Chat.Commands;

/// <summary>
/// Handles deleting previous AI response(s) for a user message and generating a new fresh response.
/// </summary>
public sealed class RegenerateAiResponseCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly IMessageStreamer _messageStreamer;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;

    public RegenerateAiResponseCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        IMessageStreamer messageStreamer,
        IAiModelServiceFactory aiModelServiceFactory,
        IAiAgentRepository aiAgentRepository,
        IUserAiModelSettingsRepository userAiModelSettingsRepository)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _messageStreamer = messageStreamer ?? throw new ArgumentNullException(nameof(messageStreamer));
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        _userAiModelSettingsRepository = userAiModelSettingsRepository ?? throw new ArgumentNullException(nameof(userAiModelSettingsRepository));
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

        var (aiService, aiAgent) = await PrepareForAiInteractionAsync(userId, chatSession, cancellationToken);
        var userSettings = await _userAiModelSettingsRepository.GetDefaultByUserIdAsync(userId, cancellationToken);

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

    private async Task<(IAiModelService AiService, AiAgent? AiAgent)> PrepareForAiInteractionAsync(
        Guid userId,
        ChatSession chatSession,
        CancellationToken cancellationToken)
    {
        AiAgent? aiAgent = null;
        if (chatSession.AiAgentId.HasValue)
        {
            aiAgent = await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken);
        }

        var aiService = _aiModelServiceFactory.GetService(
            userId,
            chatSession.AiModelId,
            chatSession.AiAgentId);

        return (aiService, aiAgent);
    }
}
