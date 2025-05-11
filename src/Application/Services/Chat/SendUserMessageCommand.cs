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
/// Handles the end-to-end flow of sending a new user message and streaming the AI response.
/// </summary>
public sealed class SendUserMessageCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly MessageStreamer _messageStreamer;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;

    public SendUserMessageCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        MessageStreamer messageStreamer,
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
        string content,
        bool enableThinking = false,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        string? safetyTolerance = null,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);

        var userMessage = await _messageService.CreateAndSaveUserMessageAsync(
            userId,
            chatSessionId,
            content,
            fileAttachments: null,
            cancellationToken);

        await _chatSessionService.UpdateChatSessionTitleAsync(chatSession, content, cancellationToken);

        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        chatSession.AddMessage(aiMessage);

        var (aiService, aiAgent) = await PrepareForAiInteractionAsync(userId, chatSession, cancellationToken);
        var userSettings = await _userAiModelSettingsRepository.GetDefaultByUserIdAsync(userId, cancellationToken);

        var requestContext = new AiRequestContext(
            UserId: userId,
            ChatSession: chatSession,
            History: new List<MessageDto>(),
            AiAgent: aiAgent,
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel,
            RequestSpecificThinking: enableThinking,
            ImageSize: imageSize,
            NumImages: numImages,
            OutputFormat: outputFormat,
            EnableSafetyChecker: enableSafetyChecker,
            SafetyTolerance: safetyTolerance);

        requestContext = requestContext with { History = HistoryBuilder.BuildHistory(requestContext, aiMessage) };

        await _messageStreamer.StreamResponseAsync(requestContext, aiMessage, aiService, cancellationToken);
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
            chatSession.CustomApiKey ?? string.Empty,
            chatSession.AiAgentId);

        return (aiService, aiAgent);
    }
}
