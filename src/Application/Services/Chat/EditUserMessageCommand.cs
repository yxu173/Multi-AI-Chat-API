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
/// Handles editing an existing user message, cleaning up subsequent AI responses, and streaming a fresh AI response.
/// </summary>
public sealed class EditUserMessageCommand
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly IMessageStreamer _messageStreamer;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;

    public EditUserMessageCommand(
        ChatSessionService chatSessionService,
        MessageService messageService,
        IMessageStreamer messageStreamer,
        IAiModelServiceFactory aiModelServiceFactory,
        IAiAgentRepository aiAgentRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IUserAiModelSettingsRepository userAiModelSettingsRepository)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _messageStreamer = messageStreamer ?? throw new ArgumentNullException(nameof(messageStreamer));
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _userAiModelSettingsRepository = userAiModelSettingsRepository ?? throw new ArgumentNullException(nameof(userAiModelSettingsRepository));
    }

    public async Task ExecuteAsync(
        Guid chatSessionId,
        Guid userId,
        Guid messageId,
        string newContent,
        string? imageSize = null,
        int? numImages = null,
        string? outputFormat = null,
        bool? enableSafetyChecker = null,
        string? safetyTolerance = null,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var messageToEdit = chatSession.Messages.FirstOrDefault(m => m.Id == messageId && m.UserId == userId && !m.IsFromAi);
        if (messageToEdit == null)
            throw new Exception("Message not found or you do not have permission to edit it.");

        var contentToUse = newContent;

        var fileAttachments = messageToEdit.FileAttachments?.ToList() ?? new List<FileAttachment>();
        List<Guid> newFileAttachmentIds = Utilities.Utilities.ExtractFileAttachmentIds(contentToUse);
        if (newFileAttachmentIds.Any())
        {
            var newFileAttachments = new List<FileAttachment>();
            foreach (var fileId in newFileAttachmentIds)
            {
                var attachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
                if (attachment != null) newFileAttachments.Add(attachment);
            }
            fileAttachments = newFileAttachments;
        }

        await _messageService.UpdateMessageContentAsync(messageToEdit, contentToUse, fileAttachments, cancellationToken);
        await new MessageEditedNotification(chatSessionId, messageId, contentToUse).PublishAsync(cancellation: cancellationToken);

        var subsequentAiMessages = chatSession.Messages
            .Where(m => m.IsFromAi && m.CreatedAt > messageToEdit.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        foreach (var subsequentAiMessage in subsequentAiMessages)
        {
            chatSession.RemoveMessage(subsequentAiMessage);
            await _messageService.DeleteMessageAsync(subsequentAiMessage.Id, cancellationToken);
        }

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
            chatSession.AiAgentId);

        return (aiService, aiAgent);
    }
}
