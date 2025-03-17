using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Polly;
using System.Net.Http;

namespace Application.Services;

public record MessageDto(string Content, bool IsFromAi, Guid MessageId);

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
        _chatSessionRepository =
            chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _aiModelServiceFactory =
            aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _tokenUsageRepository = tokenUsageRepository ?? throw new ArgumentNullException(nameof(tokenUsageRepository));
        _parallelAiProcessingService = parallelAiProcessingService ??
                                       throw new ArgumentNullException(nameof(parallelAiProcessingService));
        _chatSessionPluginRepository = chatSessionPluginRepository ??
                                       throw new ArgumentNullException(nameof(chatSessionPluginRepository));
        _pluginExecutorFactory =
            pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
        _resilienceService =
            resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
    }

    public async Task SendUserMessageWithParallelProcessingAsync(
        Guid chatSessionId,
        Guid userId,
        string content,
        IEnumerable<Guid> modelIds)
    {
        var chatSession = await _chatSessionRepository.GetByIdWithModelAsync(chatSessionId);
        if (chatSession == null) throw new Exception("Chat session not found.");


        var userMessage = Message.CreateUserMessage(userId, chatSessionId, content);
        await _messageRepository.AddAsync(userMessage);
        chatSession.AddMessage(userMessage);

        await _mediator.Publish(new MessageSentNotification(
            chatSessionId,
            new MessageDto(userMessage.Content, userMessage.IsFromAi, userMessage.Id)
        ));

        // Create placeholder AI messages for each model
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

        var messages = chatSession.Messages
            .Where(m => m.Id != userMessage.Id && !aiMessages.Values.Select(am => am.Id).Contains(m.Id))
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id))
            .ToList();

        messages.Add(new MessageDto(userMessage.Content, userMessage.IsFromAi, userMessage.Id));

        // Process all models in parallel
        var responses = await _parallelAiProcessingService.ProcessInParallelAsync(
            userId,
            messages,
            modelIds,
            CancellationToken.None);


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


                var tokenUsage = await _tokenUsageRepository.GetByChatSessionIdAsync(chatSessionId) ??
                                 await _tokenUsageRepository.AddAsync(ChatTokenUsage.Create(chatSessionId, 0, 0, 0));

                tokenUsage.UpdateTokenCounts(response.InputTokens, response.OutputTokens);
                await _tokenUsageRepository.UpdateAsync(tokenUsage);
            }
        }
    }

    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content)
    {
        var chatSession = await GetChatSessionAsync(chatSessionId);
        var userMessage = await CreateUserMessageAsync(chatSession, userId, content);
        var modifiedContent = await ExecutePluginsAsync(chatSessionId, content);
        await StreamAiResponseAsync(chatSession, userId, userMessage, modifiedContent);
    }

    private async Task<ChatSession> GetChatSessionAsync(Guid chatSessionId)
    {
        var session = await _chatSessionRepository.GetByIdWithModelAsync(chatSessionId);
        if (session == null) throw new Exception("Chat session not found.");
        return session;
    }

    private async Task<Message> CreateUserMessageAsync(ChatSession chatSession, Guid userId, string content)
    {
        if (!chatSession.Messages.Any())
        {
            var title = GenerateTitleFromContent(content);
            chatSession.UpdateTitle(title);
            await _chatSessionRepository.UpdateAsync(chatSession);
            await _mediator.Publish(new ChatTitleUpdatedNotification(chatSession.Id, title));
        }

        var message = Message.CreateUserMessage(userId, chatSession.Id, content);
        await _messageRepository.AddAsync(message);
        chatSession.AddMessage(message);
        await _mediator.Publish(new MessageSentNotification(chatSession.Id,
            new MessageDto(message.Content, false, message.Id)));
        return message;
    }

    private async Task<string> ExecutePluginsAsync(Guid chatSessionId, string content)
    {
        var plugins = await _chatSessionPluginRepository.GetActivatedPluginsAsync(chatSessionId);
        var applicablePlugins = plugins
            .Where(p => p.IsActive)
            .Select(p => _pluginExecutorFactory.GetPlugin(p.PluginId))
            .Where(p => p.CanHandle(content))
            .ToList();

        if (!applicablePlugins.Any())
            return content;

        var pluginTasks = applicablePlugins.Select(p => p.ExecuteAsync(content));
        var results = await Task.WhenAll(pluginTasks);
        var successfulResults = results.Where(r => r.Success).Select(r => r.Result).ToList();

        return successfulResults.Any()
            ? $"{content}\n\n**Plugin Results:**\n{string.Join("\n", successfulResults)}"
            : content;
    }

    private bool IsTransientError(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;
        
        return errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("too many requests", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("server error", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("retry", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("503", StringComparison.OrdinalIgnoreCase);
    }

    private async Task StreamAiResponseAsync(ChatSession chatSession, Guid userId, Message userMessage, string content)
    {
        var aiMessage = Message.CreateAiMessage(userId, chatSession.Id);
        await _messageRepository.AddAsync(aiMessage);
        chatSession.AddMessage(aiMessage);
        await _mediator.Publish(new MessageSentNotification(chatSession.Id,
            new MessageDto(aiMessage.Content, true, aiMessage.Id)));

        var aiService =
            _aiModelServiceFactory.GetService(chatSession.UserId, chatSession.AiModelId,
                chatSession.CustomApiKey ?? string.Empty);

        var messages = chatSession.Messages.Where(m => m.Id != aiMessage.Id)
            .Select(m => new MessageDto(m.Content, m.IsFromAi, m.Id)).ToList();

        messages.Add(new MessageDto(content, false, userMessage.Id));

        var responseContent = new StringBuilder();

        var tokenUsage = await _tokenUsageRepository.GetByChatSessionIdAsync(chatSession.Id) ??
                         ChatTokenUsage.Create(chatSession.Id, 0, 0, 0);

        int previousInputTokens = tokenUsage.InputTokens;

        int previousOutputTokens = tokenUsage.OutputTokens;

        decimal previousCost = tokenUsage.TotalCost;

        await foreach (var response in aiService.StreamResponseAsync(messages))
        {
            var chunk = response.Content;
            var currentInputTokens = response.InputTokens;
            var currentOutputTokens = response.OutputTokens;


            var totalInputTokens = previousInputTokens + currentInputTokens;
            var totalOutputTokens = previousOutputTokens + currentOutputTokens;


            tokenUsage.UpdateTokenCounts(
                currentInputTokens,
                currentOutputTokens
            );

            await _tokenUsageRepository.UpdateAsync(tokenUsage);


            decimal currentTotalCost = previousCost +
                                       CalculateCost(chatSession.AiModel, currentInputTokens, currentOutputTokens);


            await _mediator.Publish(new TokenUsageUpdatedNotification(
                chatSession.Id,
                totalInputTokens,
                totalOutputTokens,
                currentTotalCost
            ));

            responseContent.Append(chunk);
            aiMessage.AppendContent(chunk);
            await _messageRepository.UpdateAsync(aiMessage);
            await _mediator.Publish(new MessageChunkReceivedNotification(chatSession.Id, aiMessage.Id, chunk));
        }

        aiMessage.CompleteMessage();
        await _messageRepository.UpdateAsync(aiMessage);


        decimal finalCost = previousCost +
                            CalculateCost(chatSession.AiModel, tokenUsage.InputTokens - previousInputTokens,
                                tokenUsage.OutputTokens - previousOutputTokens);


        tokenUsage.UpdateTokenCountsAndCost(
            tokenUsage.InputTokens - previousInputTokens,
            tokenUsage.OutputTokens - previousOutputTokens,
            finalCost
        );

        await _tokenUsageRepository.UpdateAsync(tokenUsage);
    }

    private decimal CalculateCost(AiModel model, int inputTokens, int outputTokens)
    {
        var inputCost = (decimal)(inputTokens * model.InputTokenPricePer1M / 1_000_000);
        var outputCost = (decimal)(outputTokens * model.OutputTokenPricePer1M / 1_000_000);
        return inputCost + outputCost;
    }

    private string GenerateTitleFromContent(string content)
    {
        const int maxTitleLength = 50;

        if (string.IsNullOrWhiteSpace(content))
            return "New Chat";

        var title = content.Length <= maxTitleLength
            ? content
            : content.Substring(0, maxTitleLength) + "...";

        return title;
    }
}