using Application.Abstractions.Data;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
namespace Application.Services.Chat;

public class ChatService
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly MessageStreamer _messageStreamer;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IMediator _mediator;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IApplicationDbContext _dbContext;

    public ChatService(
        ChatSessionService chatSessionService,
        MessageService messageService,
        MessageStreamer messageStreamer,
        IAiModelServiceFactory aiModelServiceFactory,
        IMediator mediator,
        IAiAgentRepository aiAgentRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IApplicationDbContext dbContext)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _messageStreamer = messageStreamer ?? throw new ArgumentNullException(nameof(messageStreamer));
        _aiModelServiceFactory =
            aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        _fileAttachmentRepository = fileAttachmentRepository ??
                                    throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SendUserMessageAsync(
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
        var userMessage =
            await _messageService.CreateAndSaveUserMessageAsync(userId, chatSessionId, content,
                fileAttachments: null, cancellationToken);

        await _chatSessionService.UpdateChatSessionTitleAsync(chatSession, content, cancellationToken);
        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        chatSession.AddMessage(aiMessage);

        var (aiService, aiAgent) = await PrepareForAiInteractionAsync(userId, chatSession, cancellationToken);
        var userSettings =
            await _dbContext.UserAiModelSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

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
            SafetyTolerance: safetyTolerance
        );

        requestContext = requestContext with { History = PrepareMessageHistory(requestContext, aiMessage) };

        await _messageStreamer.StreamResponseAsync(requestContext, aiMessage, aiService, cancellationToken);
    }

    public async Task EditUserMessageAsync(
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
        var messageToEdit =
            chatSession.Messages.FirstOrDefault(m => m.Id == messageId && m.UserId == userId && !m.IsFromAi);
        if (messageToEdit == null) throw new Exception("Message not found or you do not have permission to edit it.");

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

        await _messageService.UpdateMessageContentAsync(messageToEdit, contentToUse, fileAttachments,
            cancellationToken);
        await _mediator.Publish(new MessageEditedNotification(chatSessionId, messageId, contentToUse),
            cancellationToken);

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
        var userSettings =
            await _dbContext.UserAiModelSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

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
            SafetyTolerance: safetyTolerance
        );

        requestContext = requestContext with { History = PrepareMessageHistory(requestContext, aiMessage) };

        await _messageStreamer.StreamResponseAsync(requestContext, aiMessage, aiService, cancellationToken);
    }

    public async Task RegenerateAiResponseAsync(
        Guid chatSessionId,
        Guid userId,
        Guid userMessageId,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);

        var userMessage =
            chatSession.Messages.FirstOrDefault(m => m.Id == userMessageId && m.UserId == userId && !m.IsFromAi);
        if (userMessage == null) throw new Exception("Original user message not found or access denied.");

        var aiMessageToDelete = chatSession.Messages
            .Where(m => m.IsFromAi && m.CreatedAt > userMessage.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefault();

        if (aiMessageToDelete != null)
        {
            chatSession.RemoveMessage(aiMessageToDelete);
            await _messageService.DeleteMessageAsync(aiMessageToDelete.Id, cancellationToken);
            await _mediator.Publish(new MessageDeletedNotification(chatSessionId, aiMessageToDelete.Id),
                cancellationToken);
        }

        var newAiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        chatSession.AddMessage(newAiMessage);

        var (aiService, aiAgent) = await PrepareForAiInteractionAsync(userId, chatSession, cancellationToken);
        var userSettings =
            await _dbContext.UserAiModelSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        // Log model type for debugging
        Console.WriteLine($"Using model type: {chatSession.AiModel.ModelType}");
        
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
            SafetyTolerance: null
        );

        requestContext = requestContext with { History = PrepareMessageHistory(requestContext, newAiMessage) };

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
            chatSession.CustomApiKey ?? string.Empty,
            chatSession.AiAgentId);

        return (aiService, aiAgent);
    }

    private List<MessageDto> PrepareMessageHistory(AiRequestContext context, Message currentAiMessagePlaceholder)
    {
        var chatSession = context.ChatSession;
        var aiAgent = context.AiAgent;
        var userSettings = context.UserSettings;

        int contextLimit = 0;
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

        IEnumerable<Message> limitedMessages;
        if (contextLimit > 0)
        {
            limitedMessages = messagesQuery.TakeLast(contextLimit);
        }
        else
        {
            limitedMessages = messagesQuery;
        }

        return limitedMessages
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id)
            {
                FileAttachments = m.FileAttachments?.ToList()
            })
            .ToList();
    }
}