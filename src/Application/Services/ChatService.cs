using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;

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

    public ChatService(
        IChatSessionRepository chatSessionRepository,
        IMessageRepository messageRepository,
        IAiModelServiceFactory aiModelServiceFactory,
        IMediator mediator,
        IChatTokenUsageRepository tokenUsageRepository,
        ParallelAiProcessingService parallelAiProcessingService,
        IChatSessionPluginRepository chatSessionPluginRepository,
        IPluginExecutorFactory pluginExecutorFactory)
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

    public async Task SendUserMessageAsync(Guid chatSessionId, Guid userId, string content,
        IEnumerable<string> activePluginIds = null)
    {
        var chatSession = await GetChatSessionAsync(chatSessionId);
        var userMessage = await CreateUserMessageAsync(chatSession, userId, content);
        var modifiedContent = await ExecutePluginsAsync(chatSessionId, content, activePluginIds);
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

    private async Task<string> ExecutePluginsAsync(Guid chatSessionId, string content, IEnumerable<string> activePluginIds)
    {
        var plugins = await _chatSessionPluginRepository.GetActivatedPluginsAsync(chatSessionId);
        var applicablePlugins = plugins
            .Where(p => p.IsActive && _pluginExecutorFactory.GetExecutor(p.PluginId).CanHandle(content))
            .OrderBy(p => p.Order)
            .ToList();

        if (!applicablePlugins.Any())
            return content + "\n\n[No applicable plugins available]";

        var result = content;
        bool allFailed = true;
        foreach (var plugin in applicablePlugins)
        {
            var executor = _pluginExecutorFactory.GetExecutor(plugin.PluginId);
            var pluginResult = await ExecuteWithTimeoutAsync(executor, result);
            if (pluginResult.Success)
            {
                result += $"\n\n[Plugin Result: {pluginResult.Result}]";
                allFailed = false;
            }
            else
            {
                result += $"\n\n[Plugin Error: {pluginResult.ErrorMessage}]";
            }
        }

        if (allFailed)
            result += "\n\n[All plugins failed. Please try again later.]";

        return result;
    }

    private async Task<PluginResult> ExecuteWithTimeoutAsync(IPluginExecutor executor, string content)
    {
        var task = executor.ExecuteAsync(content);
        var timeout = Task.Delay(5000);
        var completed = await Task.WhenAny(task, timeout);
        return completed == task
            ? await task
            : new PluginResult("Plugin timed out", false, "Execution exceeded 5 seconds");
    }

    private async Task StreamAiResponseAsync(ChatSession chatSession, Guid userId, Message userMessage, string content)
    {
        var aiMessage = Message.CreateAiMessage(userId, chatSession.Id);
        await _messageRepository.AddAsync(aiMessage);
        chatSession.AddMessage(aiMessage);
        await _mediator.Publish(new MessageSentNotification(chatSession.Id,
            new MessageDto(aiMessage.Content, true, aiMessage.Id)));

        var aiService =
            _aiModelServiceFactory.GetService(chatSession.UserId, chatSession.AiModelId, chatSession.CustomApiKey);
        
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