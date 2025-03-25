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
    private readonly IResilienceService _resilienceService;
    private readonly IMessageRepository _messageRepository;
    private readonly IMediator _mediator;
    private readonly IAiAgentRepository _aiAgentRepository;

    public ChatService(
        ChatSessionService chatSessionService,
        MessageService messageService,
        PluginService pluginService,
        TokenUsageService tokenUsageService,
        MessageStreamer messageStreamer,
        ParallelAiProcessingService parallelAiProcessingService,
        IAiModelServiceFactory aiModelServiceFactory,
        IResilienceService resilienceService,
        IMessageRepository messageRepository,
        IMediator mediator,
        IAiAgentRepository aiAgentRepository)
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
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
    }

    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content,
        CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var userMessage =
            await _messageService.CreateAndSaveUserMessageAsync(userId, chatSessionId, content, cancellationToken);
        await _chatSessionService.UpdateChatSessionTitleAsync(chatSession, content, cancellationToken);
        var modifiedContent = await _pluginService.ExecutePluginsAsync(chatSessionId, content, cancellationToken);

        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        var aiService =
            _aiModelServiceFactory.GetService(userId, chatSession.AiModelId, chatSession.CustomApiKey ?? string.Empty, chatSession.AiAgentId);

        // Fetch AI agent if associated with the chat session
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
        // Retrieve the chat session
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);

        // Find the user message to edit
        var messageToEdit =
            chatSession.Messages.FirstOrDefault(m => m.Id == messageId && m.UserId == userId && !m.IsFromAi);
        if (messageToEdit == null)
        {
            throw new Exception("Message not found or you do not have permission to edit it.");
        }

        // Execute plugins on the new content
        var modifiedContent = await _pluginService.ExecutePluginsAsync(chatSessionId, newContent, cancellationToken);

        // Update the message with the new content
        await _messageService.UpdateMessageContentAsync(messageToEdit, newContent, cancellationToken);

        // Find and remove subsequent AI messages
        var subsequentAiMessages = chatSession.Messages
            .Where(m => m.IsFromAi && m.CreatedAt > messageToEdit.CreatedAt)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        foreach (var subsequentAiMessage in subsequentAiMessages) // Renamed to avoid conflict
        {
            chatSession.RemoveMessage(subsequentAiMessage);
            await _messageService.DeleteMessageAsync(subsequentAiMessage.Id, cancellationToken);
        }

        // Fetch AI agent if associated with the chat session
        AiAgent aiAgent = null;
        if (chatSession.AiAgentId.HasValue)
        {
            aiAgent = await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken);
        }

        // Create and add a new AI message
        var aiMessage = await _messageService.CreateAndSaveAiMessageAsync(userId, chatSessionId, cancellationToken);
        chatSession.AddMessage(aiMessage);

        // Use the PrepareMessageHistoryForAi method instead of rebuilding the logic here
        var messages = PrepareMessageHistoryForAi(
            chatSession,
            aiMessage,
            messageToEdit,
            modifiedContent,
            aiAgent);

        // Stream the AI response
        var aiService =
            _aiModelServiceFactory.GetService(userId, chatSession.AiModelId, chatSession.CustomApiKey ?? string.Empty, chatSession.AiAgentId);
        await _messageStreamer.StreamResponseAsync(chatSession, aiMessage, aiService, messages, cancellationToken);
    }


    public async Task SendUserMessageWithParallelProcessingAsync(Guid chatSessionId, Guid userId, string content,
        IEnumerable<Guid> modelIds, CancellationToken cancellationToken = default)
    {
        var chatSession = await _chatSessionService.GetChatSessionAsync(chatSessionId, cancellationToken);
        var userMessage =
            await _messageService.CreateAndSaveUserMessageAsync(userId, chatSessionId, content, cancellationToken);
        var aiMessages =
            await CreatePlaceholderAiMessagesAsync(userId, chatSessionId, chatSession, modelIds, cancellationToken);

        // Fetch AI agent if associated with the chat session
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


    private List<MessageDto> PrepareMessageHistoryForAi(ChatSession chatSession, Message aiMessage, Message userMessage,
        string content, AiAgent aiAgent = null)
    {
        var messages = new List<MessageDto>();

        // Add system message with AI agent instructions if available
        if (aiAgent != null)
        {
            if (!string.IsNullOrEmpty(aiAgent.SystemInstructions))
            {
                messages.Add(new MessageDto($"system: {aiAgent.SystemInstructions}", true, Guid.NewGuid()));
            }
        }

        // Add chat history
        messages.AddRange(chatSession.Messages
            .Where(m => m.Id != aiMessage.Id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => {
                // Check if this message has file attachments
                var fileAttachments = m.FileAttachments?.ToList() ?? new List<FileAttachment>();
                string messageContent = m.Content;
                
                // If the message has files but no embedded file references, add them
                if (fileAttachments.Any() && 
                    !messageContent.Contains("<image") && 
                    !messageContent.Contains("<file"))
                {
                    foreach (var attachment in fileAttachments)
                    {
                        if (attachment.Base64Content != null)
                        {
                            string fileTag = attachment.FileType == FileType.Image 
                                ? $"\n<image type=\"{attachment.ContentType}\" base64=\"{attachment.Base64Content}\">\n"
                                : $"\n<file type=\"{attachment.ContentType}\" base64=\"{attachment.Base64Content}\">\n";
                            
                            messageContent += fileTag;
                        }
                    }
                }
                
                return new MessageDto(messageContent, m.IsFromAi, m.Id);
            }));

        // Add the latest user message with file attachments if present
        string latestMessageContent = content;
        var userFileAttachments = userMessage.FileAttachments?.ToList() ?? new List<FileAttachment>();
        
        // If the message has files but no embedded file references, add them
        if (userFileAttachments.Any() && 
            !latestMessageContent.Contains("<image") && 
            !latestMessageContent.Contains("<file"))
        {
            foreach (var attachment in userFileAttachments)
            {
                if (attachment.Base64Content != null)
                {
                    string fileTag = attachment.FileType == FileType.Image 
                        ? $"\n<image type=\"{attachment.ContentType}\" base64=\"{attachment.Base64Content}\">\n"
                        : $"\n<file type=\"{attachment.ContentType}\" base64=\"{attachment.Base64Content}\">\n";
                    
                    latestMessageContent += fileTag;
                }
            }
        }
        
        messages.Add(new MessageDto(latestMessageContent, false, userMessage.Id));

        return messages;
    }


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

        // Add system message with AI agent instructions if available
        if (aiAgent != null)
        {
            if (!string.IsNullOrEmpty(aiAgent.SystemInstructions))
            {
                messages.Add(new MessageDto($"system: {aiAgent.SystemInstructions}", true, Guid.NewGuid()));
            }
        }

        var aiMessageIds = aiMessages.Values.Select(am => am.Id).ToHashSet();

        // Add chat history
        messages.AddRange(chatSession.Messages
            .Where(m => m.Id != userMessage.Id && !aiMessageIds.Contains(m.Id))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id)));

        // Add the latest user message
        messages.Add(new MessageDto(userMessage.Content, userMessage.IsFromAi, userMessage.Id));

        return messages;
    }

    private async Task ProcessModelResponsesAsync(Guid chatSessionId, Dictionary<Guid, Message> aiMessages,
        IEnumerable<ParallelAiResponse> responses, CancellationToken cancellationToken = default)
    {
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
                await _tokenUsageService.UpdateTokenUsageAsync(chatSessionId, response.InputTokens,
                    response.OutputTokens, 0m, cancellationToken); // Cost calculation could be added if needed
            }
        }
    }
}