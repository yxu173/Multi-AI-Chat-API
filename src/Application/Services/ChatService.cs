using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Polly;

namespace Application.Services;

public record MessageDto(string Content, bool IsFromAi, Guid MessageId);

/// <summary>
/// Service for managing chat operations with AI models
/// </summary>
public class ChatService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IMediator _mediator;
    private readonly IChatTokenUsageRepository _tokenUsageRepository;
    private readonly ParallelAiProcessingService _parallelAiProcessingService;
    private readonly IChatSessionPluginRepository _chatSessionPluginRepository;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;
    private readonly IResilienceService _resilienceService;

    private const int MaxTitleLength = 50;
    private const int DefaultMaxTokens = 2000;

    public ChatService(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IAiModelServiceFactory aiModelServiceFactory,
        IMediator mediator,
        IChatTokenUsageRepository tokenUsageRepository,
        ParallelAiProcessingService parallelAiProcessingService,
        IChatSessionPluginRepository chatSessionPluginRepository,
        IPluginExecutorFactory pluginExecutorFactory,
        IResilienceService resilienceService)
    {
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _tokenUsageRepository = tokenUsageRepository ?? throw new ArgumentNullException(nameof(tokenUsageRepository));
        _parallelAiProcessingService = parallelAiProcessingService ?? throw new ArgumentNullException(nameof(parallelAiProcessingService));
        _chatSessionPluginRepository = chatSessionPluginRepository ?? throw new ArgumentNullException(nameof(chatSessionPluginRepository));
        _pluginExecutorFactory = pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    /// <summary>
    /// Processes user message with multiple AI models in parallel
    /// </summary>
    public async Task SendUserMessageWithParallelProcessingAsync(
        Guid chatSessionId,
        Guid userId,
        string content,
        IEnumerable<Guid> modelIds)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithModelAsync(chatSessionId)
            ?? throw new Exception("Chat session not found.");

        // Create and save user message
        var userMessage = await CreateAndSaveUserMessageAsync(userId, chatSessionId, content, chatSession);
        
        // Create placeholder messages for each AI model
        var aiMessages = await CreatePlaceholderAiMessagesAsync(userId, chatSessionId, chatSession, modelIds);

        // Get existing messages from the chat session
        var messages = GetChatHistoryForProcessing(chatSession, userMessage, aiMessages);

        // Process models in parallel and get responses
        var responses = await _parallelAiProcessingService.ProcessInParallelAsync(
            userId,
            messages,
            modelIds,
            CancellationToken.None);

        // Update AI messages with responses
        await ProcessModelResponsesAsync(chatSessionId, aiMessages, responses);
    }

    /// <summary>
    /// Sends a user message to a chat session and streams AI response
    /// </summary>
    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content)
    {
        var chatSession = await GetChatSessionAsync(chatSessionId);
        var userMessage = await CreateUserMessageAsync(chatSession, userId, content);
        var modifiedContent = await ExecutePluginsAsync(chatSessionId, content);
        await StreamAiResponseAsync(chatSession, userId, userMessage, modifiedContent);
    }

    /// <summary>
    /// Gets a chat session by ID
    /// </summary>
    private async Task<ChatSession> GetChatSessionAsync(Guid chatSessionId)
    {
        var session = await _chatSessionRepository.GetByIdWithModelAsync(chatSessionId);
        if (session == null) throw new Exception("Chat session not found.");
        return session;
    }

    /// <summary>
    /// Creates and persists a user message
    /// </summary>
    private async Task<Message> CreateUserMessageAsync(ChatSession chatSession, Guid userId, string content)
    {
        // If this is the first message, generate a title for the chat
        if (!chatSession.Messages.Any())
        {
            var title = GenerateTitleFromContent(content);
            chatSession.UpdateTitle(title);
            await _chatSessionRepository.UpdateAsync(chatSession);
            await _mediator.Publish(new ChatTitleUpdatedNotification(chatSession.Id, title));
        }

        // Create and save the user message
        var message = Message.CreateUserMessage(userId, chatSession.Id, content);
        await _messageRepository.AddAsync(message);
        chatSession.AddMessage(message);
        
        // Notify that a message has been sent
        await _mediator.Publish(new MessageSentNotification(chatSession.Id,
            new MessageDto(message.Content, false, message.Id)));
            
        return message;
    }

    /// <summary>
    /// Creates and saves a user message for parallel processing
    /// </summary>
    private async Task<Message> CreateAndSaveUserMessageAsync(Guid userId, Guid chatSessionId, string content, ChatSession chatSession)
    {
        var userMessage = Message.CreateUserMessage(userId, chatSessionId, content);
        await _messageRepository.AddAsync(userMessage);
        chatSession.AddMessage(userMessage);

        await _mediator.Publish(new MessageSentNotification(
            chatSessionId,
            new MessageDto(userMessage.Content, userMessage.IsFromAi, userMessage.Id)
        ));

        return userMessage;
    }

    /// <summary>
    /// Creates placeholder AI messages for each model in parallel processing
    /// </summary>
    private async Task<Dictionary<Guid, Message>> CreatePlaceholderAiMessagesAsync(
        Guid userId, 
        Guid chatSessionId, 
        ChatSession chatSession, 
        IEnumerable<Guid> modelIds)
    {
        var aiMessages = new Dictionary<Guid, Message>();

        foreach (var modelId in modelIds)
        {
            var aiMessage = Message.CreateAiMessage(userId, chatSessionId);
            await _messageRepository.AddAsync(aiMessage);
            chatSession.AddMessage(aiMessage);
            aiMessages[modelId] = aiMessage;

            await _mediator.Publish(new MessageSentNotification(
                chatSessionId,
                new MessageDto(aiMessage.Content, aiMessage.IsFromAi, aiMessage.Id)
            ));
        }

        return aiMessages;
    }

    /// <summary>
    /// Gets chat history for processing, excluding placeholder AI messages
    /// </summary>
    private List<MessageDto> GetChatHistoryForProcessing(
        ChatSession chatSession, 
        Message userMessage, 
        Dictionary<Guid, Message> aiMessages)
    {
        var aiMessageIds = aiMessages.Values.Select(am => am.Id).ToHashSet();
        
        var messages = chatSession.Messages
            .Where(m => m.Id != userMessage.Id && !aiMessageIds.Contains(m.Id))
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id))
            .ToList();

        messages.Add(new MessageDto(userMessage.Content, userMessage.IsFromAi, userMessage.Id));
        
        return messages;
    }

    /// <summary>
    /// Process AI model responses and update the respective placeholder messages
    /// </summary>
    private async Task ProcessModelResponsesAsync(
        Guid chatSessionId, 
        Dictionary<Guid, Message> aiMessages, 
        IEnumerable<ParallelAiResponse> responses)
    {
        foreach (var response in responses)
        {
            if (aiMessages.TryGetValue(response.ModelId, out var aiMessage))
            {
                aiMessage.AppendContent(response.Content);
                aiMessage.CompleteMessage();
                await _messageRepository.UpdateAsync(aiMessage);

                await _mediator.Publish(new MessageChunkReceivedNotification(
                    chatSessionId,
                    aiMessage.Id,
                    response.Content
                ));

                await UpdateTokenUsageAsync(chatSessionId, response.InputTokens, response.OutputTokens);
            }
        }
    }

    /// <summary>
    /// Updates token usage for a chat session
    /// </summary>
    private async Task UpdateTokenUsageAsync(Guid chatSessionId, int inputTokens, int outputTokens)
    {
        var tokenUsage = await _tokenUsageRepository.GetByChatSessionIdAsync(chatSessionId) ??
                            await _tokenUsageRepository.AddAsync(ChatTokenUsage.Create(chatSessionId, 0, 0, 0));

        tokenUsage.UpdateTokenCounts(inputTokens, outputTokens);
        await _tokenUsageRepository.UpdateAsync(tokenUsage);
    }

    /// <summary>
    /// Executes applicable plugins on the user's message content
    /// </summary>
    private async Task<string> ExecutePluginsAsync(Guid chatSessionId, string content)
    {
        var plugins = await _chatSessionPluginRepository.GetActivatedPluginsAsync(chatSessionId);
        var applicablePlugins = plugins
            .Where(p => p.IsActive)
            .Select(p => new
            {
                Plugin = _pluginExecutorFactory.GetPlugin(p.PluginId),
                Order = p.Order
            })
            .Where(p => p.Plugin.CanHandle(content))
            .OrderBy(p => p.Order)
            .ToList();

        if (!applicablePlugins.Any())
            return content;

        // Group plugins by order so plugins with same order run in parallel
        var pluginGroups = applicablePlugins.GroupBy(p => p.Order).OrderBy(g => g.Key).ToList();
        var currentContent = content;

        foreach (var group in pluginGroups)
        {
            // Execute all plugins in this order group in parallel
            var pluginTasks = group.Select(p => p.Plugin.ExecuteAsync(currentContent));
            var results = await Task.WhenAll(pluginTasks);
            var successfulResults = results.Where(r => r.Success).Select(r => r.Result).ToList();

            if (successfulResults.Any())
            {
                // Append plugin results to the content for the next order of plugins
                currentContent =
                    $"{currentContent}\n\n**Plugin Results (Order {group.Key}):**\n{string.Join("\n", successfulResults)}";
            }
        }

        return currentContent;
    }

    /// <summary>
    /// Streams AI responses for a given user message
    /// </summary>
    private async Task StreamAiResponseAsync(ChatSession chatSession, Guid userId, Message userMessage, string content)
    {
        // Create AI message placeholder
        var aiMessage = Message.CreateAiMessage(userId, chatSession.Id);
        await _messageRepository.AddAsync(aiMessage);
        chatSession.AddMessage(aiMessage);
        await _mediator.Publish(new MessageSentNotification(chatSession.Id,
            new MessageDto(aiMessage.Content, true, aiMessage.Id)));

        // Get AI service for the model
        var aiService = _aiModelServiceFactory.GetService(
            chatSession.UserId, 
            chatSession.AiModelId,
            chatSession.CustomApiKey ?? string.Empty);

        // Prepare message history
        var messages = PrepareMessageHistoryForAi(chatSession, aiMessage, userMessage, content);

        // Get or create token usage record
        var (tokenUsage, previousInputTokens, previousOutputTokens, previousCost) = 
            await GetOrCreateTokenUsageAsync(chatSession.Id);

        // Process streaming response
        var responseContent = new StringBuilder();
        await foreach (var response in aiService.StreamResponseAsync(messages))
        {
            await ProcessStreamResponseChunkAsync(
                chatSession, 
                aiMessage, 
                response, 
                responseContent, 
                tokenUsage,
                previousInputTokens,
                previousOutputTokens,
                previousCost);
        }

        // Complete the AI message
        await FinalizeAiMessageAsync(
            aiMessage, 
            tokenUsage, 
            chatSession,
            previousInputTokens,
            previousOutputTokens,
            previousCost);
    }

    /// <summary>
    /// Prepares message history to send to AI
    /// </summary>
    private List<MessageDto> PrepareMessageHistoryForAi(
        ChatSession chatSession, 
        Message aiMessage, 
        Message userMessage, 
        string content)
    {
        var messages = chatSession.Messages
            .Where(m => m.Id != aiMessage.Id)
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id))
            .ToList();

        // Add the modified user message content (which may include plugin results)
        messages.Add(new MessageDto(content, false, userMessage.Id));
        
        return messages;
    }

    /// <summary>
    /// Gets or creates a token usage record for a chat session
    /// </summary>
    private async Task<(ChatTokenUsage TokenUsage, int PreviousInputTokens, int PreviousOutputTokens, decimal PreviousCost)> 
        GetOrCreateTokenUsageAsync(Guid chatSessionId)
    {
        var tokenUsage = await _tokenUsageRepository.GetByChatSessionIdAsync(chatSessionId) ??
                          await _tokenUsageRepository.AddAsync(ChatTokenUsage.Create(chatSessionId, 0, 0, 0));

        return (tokenUsage, tokenUsage.InputTokens, tokenUsage.OutputTokens, tokenUsage.TotalCost);
    }

    /// <summary>
    /// Processes a chunk of streaming response from AI
    /// </summary>
    private async Task ProcessStreamResponseChunkAsync(
        ChatSession chatSession,
        Message aiMessage,
        StreamResponse response,
        StringBuilder responseContent,
        ChatTokenUsage tokenUsage,
        int previousInputTokens,
        int previousOutputTokens,
        decimal previousCost)
    {
        var chunk = response.Content;
        var currentInputTokens = response.InputTokens;
        var currentOutputTokens = response.OutputTokens;

        var totalInputTokens = previousInputTokens + currentInputTokens;
        var totalOutputTokens = previousOutputTokens + currentOutputTokens;

        // Update token counts
        tokenUsage.UpdateTokenCounts(currentInputTokens, currentOutputTokens);
        await _tokenUsageRepository.UpdateAsync(tokenUsage);

        // Calculate current cost
        decimal currentTotalCost = previousCost +
                               CalculateCost(chatSession.AiModel, currentInputTokens, currentOutputTokens);

        // Notify of token usage updates
        await _mediator.Publish(new TokenUsageUpdatedNotification(
            chatSession.Id,
            totalInputTokens,
            totalOutputTokens,
            currentTotalCost
        ));

        // Append response chunk to message
        responseContent.Append(chunk);
        aiMessage.AppendContent(chunk);
        await _messageRepository.UpdateAsync(aiMessage);
        await _mediator.Publish(new MessageChunkReceivedNotification(chatSession.Id, aiMessage.Id, chunk));
    }

    /// <summary>
    /// Finalizes an AI message after streaming is complete
    /// </summary>
    private async Task FinalizeAiMessageAsync(
        Message aiMessage, 
        ChatTokenUsage tokenUsage,
        ChatSession chatSession,
        int previousInputTokens,
        int previousOutputTokens,
        decimal previousCost)
    {
        aiMessage.CompleteMessage();
        await _messageRepository.UpdateAsync(aiMessage);

        // Calculate final cost
        decimal finalCost = previousCost +
                        CalculateCost(chatSession.AiModel, 
                            tokenUsage.InputTokens - previousInputTokens,
                            tokenUsage.OutputTokens - previousOutputTokens);

        // Update final token counts and cost
        tokenUsage.UpdateTokenCountsAndCost(
            tokenUsage.InputTokens - previousInputTokens,
            tokenUsage.OutputTokens - previousOutputTokens,
            finalCost
        );

        await _tokenUsageRepository.UpdateAsync(tokenUsage);
    }

    /// <summary>
    /// Calculates cost based on token usage and model pricing
    /// </summary>
    private decimal CalculateCost(AiModel model, int inputTokens, int outputTokens)
    {
        var inputCost = (decimal)(inputTokens * model.InputTokenPricePer1M / 1_000_000);
        var outputCost = (decimal)(outputTokens * model.OutputTokenPricePer1M / 1_000_000);
        return inputCost + outputCost;
    }

    /// <summary>
    /// Generates a title from the first message content
    /// </summary>
    private string GenerateTitleFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "New Chat";

        var title = content.Length <= MaxTitleLength
            ? content
            : content.Substring(0, MaxTitleLength) + "...";

        return title;
    }
}