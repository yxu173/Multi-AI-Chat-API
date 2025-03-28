using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;

namespace Application.Services;

//public record MessageDto(string Content, bool IsFromAi, Guid MessageId);

public class ChatService
{
    private readonly ChatSessionService _chatSessionService;
    private readonly MessageService _messageService;
    private readonly PluginService _pluginService;
    private readonly TokenUsageService _tokenUsageService;
    private readonly MessageStreamer _messageStreamer;
    private readonly ParallelAiProcessingService _parallelAiProcessingService;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IMessageRepository _messageRepository;
    private readonly IMediator _mediator;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IFileAttachmentRepository _fileAttachmentRepository;

    public ChatService(
        ChatSessionService chatSessionService,
        MessageService messageService,
        PluginService pluginService,
        TokenUsageService tokenUsageService,
        MessageStreamer messageStreamer,
        ParallelAiProcessingService parallelAiProcessingService,
        IAiModelServiceFactory aiModelServiceFactory,
        IMessageRepository messageRepository,
        IMediator mediator,
        IAiAgentRepository aiAgentRepository,
        IFileAttachmentRepository fileAttachmentRepository)
    {
        _chatSessionService = chatSessionService ?? throw new ArgumentNullException(nameof(chatSessionService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _tokenUsageService = tokenUsageService ?? throw new ArgumentNullException(nameof(tokenUsageService));
        _messageStreamer = messageStreamer ?? throw new ArgumentNullException(nameof(messageStreamer));
        _parallelAiProcessingService = parallelAiProcessingService ??
                                       throw new ArgumentNullException(nameof(parallelAiProcessingService));
        _aiModelServiceFactory =
            aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
    }

    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var userMessage =
            await _messageService.CreateAndSaveUserMessageAsync(userId, chatSessionId, content, 
                fileAttachments: null, cancellationToken);

        await _chatSessionService.UpdateChatSessionTitleAsync(chatSession, content, cancellationToken);
        var modifiedContent = await _pluginService.ExecutePluginsAsync(chatSessionId, content, cancellationToken);
        

        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        var aiService =
            _aiModelServiceFactory.GetService(userId, chatSession.AiModelId,
                chatSession.CustomApiKey ?? string.Empty, chatSession.AiAgentId);

        AiAgent aiAgent = null;
        if (chatSession.AiAgentId.HasValue)
        {
            aiAgent = await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken);
        }

        var messages = PrepareMessageHistoryForAi(chatSession, aiMessage, userMessage, modifiedContent, aiAgent);

        await _messageStreamer.StreamResponseAsync(chatSession, aiMessage, aiService, messages, cancellationToken);
    }

    public async Task EditUserMessageAsync(Guid chatSessionId, Guid userId, Guid messageId, string newContent,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var messageToEdit =
            chatSession.Messages.FirstOrDefault(m => m.Id == messageId && m.UserId == userId && !m.IsFromAi);
        if (messageToEdit == null)
        {
            throw new Exception("Message not found or you do not have permission to edit it.");
        }

        var modifiedContent = await _pluginService.ExecutePluginsAsync(chatSessionId, newContent, cancellationToken);
        
        // Keep the existing file attachments when editing
        var fileAttachments = messageToEdit.FileAttachments?.ToList();
        
        // Check for embedded file references in the new content
        List<Guid> newFileAttachmentIds = ExtractFileAttachmentIds(newContent);
        if (newFileAttachmentIds.Any())
        {
            // Get file attachment objects for the new IDs
            var newFileAttachments = new List<FileAttachment>();
            foreach (var fileId in newFileAttachmentIds)
            {
                var attachment = await _fileAttachmentRepository.GetByIdAsync(fileId, cancellationToken);
                if (attachment != null)
                {
                    newFileAttachments.Add(attachment);
                }
            }
            
            fileAttachments = newFileAttachments;
        }
        
        await _messageService.UpdateMessageContentAsync(messageToEdit, newContent, fileAttachments, cancellationToken);
        await _mediator.Publish(new MessageEditedNotification(chatSessionId, messageId, newContent), cancellationToken);
        
        var subsequentAiMessages = chatSession.Messages
            .Where(m => m.IsFromAi && m.CreatedAt > messageToEdit.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        foreach (var subsequentAiMessage in subsequentAiMessages)
        {
            chatSession.RemoveMessage(subsequentAiMessage);
            await _messageService.DeleteMessageAsync(subsequentAiMessage.Id, cancellationToken);
        }

        AiAgent aiAgent = null;
        if (chatSession.AiAgentId.HasValue)
        {
            aiAgent = await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken);
        }

        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        chatSession.AddMessage(aiMessage);

        var messages = PrepareMessageHistoryForAi(chatSession, aiMessage, messageToEdit, modifiedContent, aiAgent);
        var aiService =
            _aiModelServiceFactory.GetService(userId, chatSession.AiModelId, chatSession.CustomApiKey ?? string.Empty,
                chatSession.AiAgentId);
        await _messageStreamer.StreamResponseAsync(chatSession, aiMessage, aiService, messages, cancellationToken);
    }

    private List<Guid> ExtractFileAttachmentIds(string content)
    {
        var fileIds = new List<Guid>();
        
        var imageMatches = System.Text.RegularExpressions.Regex.Matches(content, @"<(image|file):[^>]*?/api/file/([0-9a-fA-F-]{36})");
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
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var userMessage =
            await _messageService.CreateAndSaveUserMessageAsync(userId, chatSessionId, content,
                fileAttachments, cancellationToken);
        var aiMessages =
            await CreatePlaceholderAiMessagesAsync(userId, chatSessionId, chatSession, modelIds, cancellationToken);

        AiAgent aiAgent = null;
        if (chatSession.AiAgentId.HasValue)
        {
            aiAgent = await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken);
        }

        var messages = GetChatHistoryForProcessing(chatSession, userMessage, aiMessages, aiAgent);
        var responses =
            await _parallelAiProcessingService.ProcessInParallelAsync(userId, messages, modelIds, cancellationToken);
        await ProcessModelResponsesAsync(chatSessionId, aiMessages, responses, cancellationToken);
    }

    private List<MessageDto> PrepareMessageHistoryForAi(ChatSession chatSession, Message aiMessage, 
        Message userMessage, string content, AiAgent aiAgent = null)
    {
        const int maxTokens = 100000;
        int currentTokens = 0;
        var messages = new List<MessageDto>();

        if (aiAgent != null && !string.IsNullOrEmpty(aiAgent.SystemInstructions))
        {
            var systemMsg = new MessageDto(aiAgent.SystemInstructions, true, Guid.NewGuid());
            messages.Add(systemMsg);
            currentTokens += EstimateTokens(systemMsg.Content);
        }

        var historyMessages = chatSession.Messages
            .Where(m => m.Id != aiMessage.Id && m.Id != userMessage.Id)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();

        foreach (var msg in historyMessages)
        {
            var msgTokens = EstimateTokens(msg.Content) + (msg.FileAttachments?.Sum(f => EstimateFileTokens(f)) ?? 0);
            
            if (currentTokens + msgTokens > maxTokens) break;
            
            if (messages.Count > 0)
            {
                messages.Insert(1, new MessageDto(msg.Content, msg.IsFromAi, msg.Id));
            }
            else
            {
                messages.Add(new MessageDto(msg.Content, msg.IsFromAi, msg.Id));
            }
            
            currentTokens += msgTokens;
        }

        // Add current message
        messages.Add(new MessageDto(content, false, userMessage.Id));
        return messages;
    }

    private int EstimateTokens(string text) => (int)(text.Length * 0.75);
    private int EstimateFileTokens(FileAttachment file) => (int)(file.FileSize * 0.75);

    private async Task<Dictionary<Guid, Message>> CreatePlaceholderAiMessagesAsync(Guid userId, Guid chatSessionId,
        ChatSession chatSession, IEnumerable<Guid> modelIds, CancellationToken cancellationToken = default)
    {
        var aiMessages = new Dictionary<Guid, Message>();
        foreach (var modelId in modelIds)
        {
            var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
            chatSession.AddMessage(aiMessage);
            aiMessages[modelId] = aiMessage;
        }

        return aiMessages;
    }

    private List<MessageDto> GetChatHistoryForProcessing(ChatSession chatSession, Message userMessage,
        Dictionary<Guid, Message> aiMessages, AiAgent aiAgent = null)
    {
        var messages = new List<MessageDto>();

        if (aiAgent != null && !string.IsNullOrEmpty(aiAgent.SystemInstructions))
        {
            messages.Add(new MessageDto($"system: {aiAgent.SystemInstructions}", true, Guid.NewGuid()));
        }

        var aiMessageIds = aiMessages.Values.Select(am => am.Id).ToHashSet();

        messages.AddRange(chatSession.Messages
            .Where(m => m.Id != userMessage.Id && !aiMessageIds.Contains(m.Id))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id)));

        messages.Add(new MessageDto(userMessage.Content, userMessage.IsFromAi, userMessage.Id));

        return messages;
    }

    private const int MIN_TOKEN_UPDATE_THRESHOLD = 5;
    private Dictionary<Guid, int> _accumulatedInputTokens = new Dictionary<Guid, int>();
    private Dictionary<Guid, int> _accumulatedOutputTokens = new Dictionary<Guid, int>();
    private Dictionary<Guid, int> _previousInputTokens = new Dictionary<Guid, int>();
    private Dictionary<Guid, int> _previousOutputTokens = new Dictionary<Guid, int>();

    private async Task ProcessModelResponsesAsync(Guid chatSessionId, Dictionary<Guid, Message> aiMessages,
        IEnumerable<ParallelAiResponse> responses, CancellationToken cancellationToken = default)
    {
        foreach (var modelId in aiMessages.Keys)
        {
            if (!_accumulatedInputTokens.ContainsKey(modelId))
            {
                _accumulatedInputTokens[modelId] = 0;
                _accumulatedOutputTokens[modelId] = 0;
                _previousInputTokens[modelId] = 0;
                _previousOutputTokens[modelId] = 0;
            }
        }

        var tokenUsage = await _tokenUsageService.GetOrCreateTokenUsageAsync(chatSessionId, cancellationToken);

        foreach (var response in responses)
        {
            if (aiMessages.TryGetValue(response.ModelId, out var aiMessage))
            {
                aiMessage.AppendContent(response.Content);
                aiMessage.CompleteMessage();
                await _messageRepository.UpdateAsync(aiMessage, cancellationToken);

                await _mediator.Publish(
                    new MessageChunkReceivedNotification(chatSessionId, aiMessage.Id, response.Content),
                    cancellationToken);

                var currentInputTokens = response.InputTokens;
                var currentOutputTokens = response.OutputTokens;

                int inputTokenDelta = Math.Max(0, currentInputTokens - _previousInputTokens[response.ModelId]);
                int outputTokenDelta = Math.Max(0, currentOutputTokens - _previousOutputTokens[response.ModelId]);

                _accumulatedInputTokens[response.ModelId] += inputTokenDelta;
                _accumulatedOutputTokens[response.ModelId] += outputTokenDelta;

                bool shouldUpdateTokens =
                    _accumulatedInputTokens[response.ModelId] >= MIN_TOKEN_UPDATE_THRESHOLD ||
                    _accumulatedOutputTokens[response.ModelId] >= MIN_TOKEN_UPDATE_THRESHOLD;

                if (shouldUpdateTokens)
                {
                    decimal cost = await CalculateCostAsync(
                        chatSessionId,
                        _accumulatedInputTokens[response.ModelId],
                        _accumulatedOutputTokens[response.ModelId],
                        cancellationToken);

                    await _tokenUsageService.UpdateTokenUsageAsync(
                        chatSessionId,
                        _accumulatedInputTokens[response.ModelId],
                        _accumulatedOutputTokens[response.ModelId],
                        cost,
                        cancellationToken);

                    _accumulatedInputTokens[response.ModelId] = 0;
                    _accumulatedOutputTokens[response.ModelId] = 0;
                }

                _previousInputTokens[response.ModelId] = currentInputTokens;
                _previousOutputTokens[response.ModelId] = currentOutputTokens;
            }
        }

        foreach (var modelId in aiMessages.Keys)
        {
            if (_accumulatedInputTokens[modelId] > 0 || _accumulatedOutputTokens[modelId] > 0)
            {
                decimal cost = await CalculateCostAsync(
                    chatSessionId,
                    _accumulatedInputTokens[modelId],
                    _accumulatedOutputTokens[modelId],
                    cancellationToken);

                await _tokenUsageService.UpdateTokenUsageAsync(
                    chatSessionId,
                    _accumulatedInputTokens[modelId],
                    _accumulatedOutputTokens[modelId],
                    cost,
                    cancellationToken);

                _accumulatedInputTokens[modelId] = 0;
                _accumulatedOutputTokens[modelId] = 0;
            }
        }
    }

    private async Task<decimal> CalculateCostAsync(Guid chatSessionId, int inputTokens, int outputTokens,
        CancellationToken cancellationToken)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var inputCost = (decimal)(inputTokens * chatSession.AiModel.InputTokenPricePer1M / 1_000_000);
        var outputCost = (decimal)(outputTokens * chatSession.AiModel.OutputTokenPricePer1M / 1_000_000);
        return inputCost + outputCost;
    }
}