using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Application.Abstractions.Data;
using Domain.Aggregates.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

//public record MessageDto(string Content, bool IsFromAi, Guid MessageId);

public class ChatService
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly PluginService _pluginService;
    private readonly MessageStreamer _messageStreamer;
    private readonly ParallelAiProcessingService _parallelAiProcessingService;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IMediator _mediator;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        ChatSessionService chatSessionService,
        MessageService messageService,
        PluginService pluginService,
        MessageStreamer messageStreamer,
        ParallelAiProcessingService parallelAiProcessingService,
        IAiModelServiceFactory aiModelServiceFactory,
        IMediator mediator,
        IAiAgentRepository aiAgentRepository,
        IFileAttachmentRepository fileAttachmentRepository,
        IApplicationDbContext dbContext,
        ILogger<ChatService> logger)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _messageStreamer = messageStreamer ?? throw new ArgumentNullException(nameof(messageStreamer));
        _parallelAiProcessingService = parallelAiProcessingService ??
                                       throw new ArgumentNullException(nameof(parallelAiProcessingService));
        _aiModelServiceFactory =
            aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content,
        bool enableThinking = false,
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
        var userSettings = await _dbContext.UserAiModelSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        var history = PrepareMessageHistory(chatSession, userMessage, aiMessage);

        var requestContext = new AiRequestContext(
            UserId: userId,
            ChatSession: chatSession,
            History: history,
            AiAgent: aiAgent, 
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel,
            RequestSpecificThinking : enableThinking
        );

        await _messageStreamer.StreamResponseAsync(requestContext, aiMessage, aiService, cancellationToken);
    }

    public async Task EditUserMessageAsync(Guid chatSessionId, Guid userId, Guid messageId, string newContent,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var messageToEdit = chatSession.Messages.FirstOrDefault(m => m.Id == messageId && m.UserId == userId && !m.IsFromAi);
        if (messageToEdit == null) throw new Exception("Message not found or you do not have permission to edit it.");

        var contentToUse = newContent;
        
        var fileAttachments = messageToEdit.FileAttachments?.ToList() ?? new List<FileAttachment>();
        List<Guid> newFileAttachmentIds = ExtractFileAttachmentIds(contentToUse);
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
        await _mediator.Publish(new MessageEditedNotification(chatSessionId, messageId, contentToUse), cancellationToken);
        
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
        var userSettings = await _dbContext.UserAiModelSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        var history = PrepareMessageHistory(chatSession, messageToEdit, aiMessage);

        var requestContext = new AiRequestContext(
            UserId: userId,
            ChatSession: chatSession,
            History: history,
            AiAgent: aiAgent,
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel
        );

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

    private List<MessageDto> PrepareMessageHistory(ChatSession chatSession, Message userMessageTrigger, Message currentAiMessagePlaceholder)
    {
        return chatSession.Messages
            .Where(m => m.Id != currentAiMessagePlaceholder.Id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id) { 
                 FileAttachments = m.FileAttachments?.ToList()
            })
            .ToList();
    }

    private List<Guid> ExtractFileAttachmentIds(string content)
    {
        var fileIds = new List<Guid>();
        
        var imageMatches = System.Text.RegularExpressions.Regex.Matches(content, @"<(image|file):[^>]*?/api/file/([0-9a-fA-F-]{36})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in imageMatches)
        {
            if (Guid.TryParse(match.Groups[2].Value, out Guid fileId))
            {
                fileIds.Add(fileId);
            }
        }
        
        return fileIds;
    }

    public async Task SendUserMessageWithParallelProcessingAsync(Guid chatSessionId, Guid userId, string content,
        IEnumerable<Guid> modelIds, IEnumerable<FileAttachment>? fileAttachments = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning($"{nameof(SendUserMessageWithParallelProcessingAsync)} requires refactoring to align with new AI interaction pattern.");
        throw new NotImplementedException($"{nameof(SendUserMessageWithParallelProcessingAsync)} needs refactoring.");
    }
}