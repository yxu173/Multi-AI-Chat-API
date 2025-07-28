using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Infrastructure;
using Application.Services.Messaging;
using Application.Services.Resilience;
using Application.Services.TokenUsage;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using FastEndpoints;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Application.Services.Streaming;

public record StreamingRequest(
    Guid ChatSessionId,
    Guid UserId,
    Guid AiMessageId,
    List<Message>? History = null,
    bool EnableThinking = false,
    string? ImageSize = null,
    int? NumImages = null,
    string? OutputFormat = null,
    bool EnableDeepSearch = false,
    double? Temperature = null,
    int? OutputToken = null
);

public record StreamingResult(
    int InputTokens,
    int OutputTokens,
    bool ConversationCompleted,
    string? AccumulatedThinkingContent
);

public class StreamingService : IStreamingService
{
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IProviderKeyManagementService _providerKeyManagementService;
    private readonly ILogger<StreamingService> _logger;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly TokenUsageService _tokenUsageService;
    private readonly IMessageRepository _messageRepository;
    private readonly IAiMessageFinalizer _aiMessageFinalizer;
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly ToolCallHandler _toolCallHandler;
    private readonly IStreamingContextService _streamingContextService;
    private readonly IStreamingResilienceHandler _resilienceHandler;
    private readonly StreamingOptions _options;
    private readonly IBackgroundJobClient _backgroundJobClient;

    private readonly ConcurrentQueue<StringBuilder> _stringBuilderPool = new();
    private readonly ConcurrentQueue<List<MessageDto>> _messageListPool = new();
    private readonly ConcurrentQueue<Dictionary<int, ToolCallState>> _toolCallStatePool = new();

    private static readonly ActivitySource ActivitySource = new("Application.Services.Streaming.StreamingService", "1.0.0");

    public StreamingService(
        IAiModelServiceFactory aiModelServiceFactory,
        IProviderKeyManagementService providerKeyManagementService,
        ILogger<StreamingService> logger,
        StreamingOperationManager streamingOperationManager,
        TokenUsageService tokenUsageService,
        IMessageRepository messageRepository,
        IAiMessageFinalizer aiMessageFinalizer,
        IAiRequestHandler aiRequestHandler,
        ToolCallHandler toolCallHandler,
        IStreamingContextService streamingContextService,
        IStreamingResilienceHandler resilienceHandler,
        IBackgroundJobClient backgroundJobClient,
        IOptions<StreamingOptions> options)
    {
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _providerKeyManagementService = providerKeyManagementService ?? throw new ArgumentNullException(nameof(providerKeyManagementService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _streamingOperationManager = streamingOperationManager ?? throw new ArgumentNullException(nameof(streamingOperationManager));
        _tokenUsageService = tokenUsageService ?? throw new ArgumentNullException(nameof(tokenUsageService));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _aiMessageFinalizer = aiMessageFinalizer ?? throw new ArgumentNullException(nameof(aiMessageFinalizer));
        _aiRequestHandler = aiRequestHandler ?? throw new ArgumentNullException(nameof(aiRequestHandler));
        _toolCallHandler = toolCallHandler ?? throw new ArgumentNullException(nameof(toolCallHandler));
        _streamingContextService = streamingContextService ?? throw new ArgumentNullException(nameof(streamingContextService));
        _resilienceHandler = resilienceHandler ?? throw new ArgumentNullException(nameof(resilienceHandler));
        _backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<StreamingResult> StreamResponseAsync(StreamingRequest request, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(StreamingService.StreamResponseAsync));
        activity?.SetTag("chat_session.id", request.ChatSessionId.ToString());
        activity?.SetTag("user.id", request.UserId.ToString());
        activity?.SetTag("ai_message.id", request.AiMessageId.ToString());

        try
        {
            var requestContext = await _streamingContextService.BuildContextAsync(request, cancellationToken);
            var chatSession = requestContext.ChatSession;
            var aiMessage = chatSession.Messages.First(m => m.Id == request.AiMessageId);

            var cts = new CancellationTokenSource();
            _streamingOperationManager.RegisterOperation(aiMessage.Id, cts);

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            var linkedToken = linkedCts.Token;

            try
            {
                var result = await _resilienceHandler.ExecuteWithRetriesAsync(
                    () => ProcessStreamInternalAsync(requestContext, aiMessage, linkedToken),
                    requestContext,
                    aiMessage,
                    linkedToken);
                
                // Handle thinking content
                if (!string.IsNullOrEmpty(result.AccumulatedThinkingContent))
                {
                    await UpdateMessageThinkingContentAsync(aiMessage, result.AccumulatedThinkingContent, CancellationToken.None);
                    await PublishThinkingContentUpdatedAsync(request.ChatSessionId, aiMessage.Id, result.AccumulatedThinkingContent);
                }

                // Handle token usage
                if (result.InputTokens > 0 || result.OutputTokens > 0)
                {
                    decimal finalCost = chatSession.AiModel.CalculateCost(result.InputTokens, result.OutputTokens);
                    _logger.LogInformation(
                        "Updating final accumulated token usage for ChatSession {ChatSessionId}: Input={InputTokens}, Output={OutputTokens}, Cost={Cost}",
                        request.ChatSessionId, result.InputTokens, result.OutputTokens, finalCost);
                    await _tokenUsageService.UpdateTokenUsageAsync(request.ChatSessionId, result.InputTokens, result.OutputTokens,
                        finalCost, CancellationToken.None);
                }

                // Enqueue background job to increment user subscription usage
                _backgroundJobClient.Enqueue<ISubscriptionUsageJob>(j => j.IncrementUsageAsync(request.UserId, chatSession.AiModel.RequestCost));

                var persistenceToken = cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
                await _aiMessageFinalizer.FinalizeProgressingMessageAsync(aiMessage, result.ConversationCompleted, persistenceToken);

                return result;
            }
            catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
            {
                bool wasDirectUserCancellation = cancellationToken.IsCancellationRequested;
                await _aiMessageFinalizer.FinalizeAfterCancellationAsync(aiMessage, wasDirectUserCancellation, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                await _aiMessageFinalizer.FinalizeAfterErrorAsync(aiMessage, ex, CancellationToken.None);
                throw;
            }
            finally
            {
                _streamingOperationManager.StopStreaming(aiMessage.Id);
                linkedCts.Dispose();
                cts.Dispose();
                _logger.LogInformation("Streaming operation finished or cleaned up for message {MessageId}", aiMessage.Id);
            }
        }
        finally
        {
        }
    }

    private async Task<StreamingResult> ProcessStreamInternalAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        CancellationToken cancellationToken)
    {
        var serviceContext = await _aiModelServiceFactory.GetServiceContextAsync(
            requestContext.UserId,
            requestContext.ChatSession.AiModelId,
            requestContext.ChatSession.AiAgentId,
            cancellationToken);

        var result = await ProcessConversationTurnAsync(
            requestContext,
            aiMessage,
            serviceContext.Service,
            cancellationToken,
            serviceContext.ApiKey?.Id);
        if (serviceContext.ApiKey != null)
        {
            await _providerKeyManagementService.ReportKeySuccessAsync(serviceContext.ApiKey.Id, CancellationToken.None);
        }
        return result;
    }

    private async Task<StreamingResult> ProcessConversationTurnAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        var finalContent = _options.EnableObjectPooling ? RentStringBuilder() : new StringBuilder();
        var toolResults = _options.EnableObjectPooling ? RentMessageList() : new List<MessageDto>();
        MessageDto? toolRequestMsg = null;
        string? thinkingContent = null;
        int inTokens = 0;
        int outTokens = 0;
        bool conversationCompleted = false;
        int turn = 0;

        var baseHistory = new List<MessageDto>(requestContext.History);
        var modelType = requestContext.SpecificModel.ModelType;
        var allowToolCalls = modelType == ModelType.OpenAi || modelType == ModelType.Anthropic || 
                           modelType == ModelType.Gemini || modelType == ModelType.DeepSeek || 
                           modelType == ModelType.Grok;

        // Notification batching
        var notificationBatch = new List<(string Type, object Data)>();
        var lastNotificationTime = DateTime.UtcNow;
        Timer? idleFlushTimer = null;
        object notificationLock = new();
        bool batchFlushed = false;
        bool isFirstChunk = true;

        void FlushBatchIfNeeded()
        {
            lock (notificationLock)
            {
                if (notificationBatch.Count > 0 && !batchFlushed)
                {
                    FlushNotificationBatchAsync(notificationBatch, requestContext.ChatSession.Id, aiMessage.Id, cancellationToken).GetAwaiter().GetResult();
                    notificationBatch.Clear();
                    lastNotificationTime = DateTime.UtcNow;
                    batchFlushed = true;
                }
            }
        }

        void ResetIdleFlushTimer()
        {
            if (idleFlushTimer != null)
            {
                idleFlushTimer.Change(_options.NotificationBatchIdleFlushMs, Timeout.Infinite);
            }
        }

        if (_options.EnableNotificationBatching)
        {
            idleFlushTimer = new Timer(_ =>
            {
                FlushBatchIfNeeded();
            }, null, Timeout.Infinite, Timeout.Infinite);
        }

        try
        {
            while (turn < _options.MaxConversationTurns && !conversationCompleted && !cancellationToken.IsCancellationRequested)
            {
                turn++;

                var historyTurn = new List<MessageDto>(baseHistory);
                if (toolRequestMsg != null)
                {
                    historyTurn.Add(toolRequestMsg);
                    historyTurn.AddRange(toolResults);
                }

                var ctxTurn = requestContext with { History = historyTurn };
                var payload = await _aiRequestHandler.PrepareRequestPayloadAsync(ctxTurn, cancellationToken);

                toolResults.Clear();
                toolRequestMsg = null;
                finalContent.Clear();

                var toolCallStates = _options.EnableObjectPooling ? RentToolCallStateDictionary() : new Dictionary<int, ToolCallState>();
                var thinkingContentBuilder = _options.EnableObjectPooling ? RentStringBuilder() : new StringBuilder();
                List<ParsedToolCall>? completedToolCalls = null;
                bool turnCompleted = false;

                try
                {
                    var stream = aiService.StreamResponseAsync(payload, cancellationToken, providerApiKeyId);

                    await foreach (var chunk in stream.WithCancellation(cancellationToken))
                    {
                        batchFlushed = false;
                        if (_options.EnableNotificationBatching)
                        {
                            ResetIdleFlushTimer();
                        }
                        
                        if (chunk.InputTokens.HasValue) inTokens = chunk.InputTokens.Value;
                        if (chunk.OutputTokens.HasValue) outTokens = chunk.OutputTokens.Value;

                        if (!string.IsNullOrEmpty(chunk.TextDelta))
                        {
                            finalContent.Append(chunk.TextDelta);
                            notificationBatch.Add(("text", chunk.TextDelta));
                            if (isFirstChunk)
                            {
                                await FlushNotificationBatchAsync(notificationBatch, requestContext.ChatSession.Id, aiMessage.Id, cancellationToken);
                                notificationBatch.Clear();
                                lastNotificationTime = DateTime.UtcNow;
                                batchFlushed = true;
                                isFirstChunk = false;
                            }
                            else if (await TryFlushBatchAsync(notificationBatch, lastNotificationTime, requestContext.ChatSession.Id, aiMessage.Id, cancellationToken))
                            {
                                lastNotificationTime = DateTime.UtcNow;
                                batchFlushed = true;
                                if (_options.EnableNotificationBatching)
                                {
                                    idleFlushTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                                }
                            }
                        }

                        bool supportsThinking = requestContext.RequestSpecificThinking ?? requestContext.ChatSession.EnableThinking;
                        if (supportsThinking && !string.IsNullOrEmpty(chunk.ThinkingDelta))
                        {
                            thinkingContentBuilder.Append(chunk.ThinkingDelta);
                            
                            notificationBatch.Add(("thinking", chunk.ThinkingDelta));
                            if (await TryFlushBatchAsync(notificationBatch, lastNotificationTime, requestContext.ChatSession.Id, aiMessage.Id, cancellationToken))
                            {
                                lastNotificationTime = DateTime.UtcNow;
                                batchFlushed = true;
                                if (_options.EnableNotificationBatching)
                                {
                                    idleFlushTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                                }
                            }
                        }

                        if (chunk.ToolCallInfo != null)
                        {
                            var toolInfo = chunk.ToolCallInfo;
                            if (!toolCallStates.TryGetValue(toolInfo.Index, out var state))
                            {
                                state = new ToolCallState();
                                toolCallStates[toolInfo.Index] = state;
                            }
                            if (toolInfo.Id != null) state.Id = toolInfo.Id;
                            if (toolInfo.Name != null) state.Name = toolInfo.Name;
                            if (toolInfo.ArgumentChunk != null) state.ArgumentBuffer.Append(toolInfo.ArgumentChunk);
                        }

                        if (!string.IsNullOrEmpty(chunk.UrlCitation))
                        {
                            var parts = chunk.UrlCitation.Split('|', 2);
                            if (parts.Length == 2)
                            {
                                var title = parts[0];
                                var url = parts[1];
                                await new DeepSearchUrlCitationNotification(requestContext.ChatSession.Id, title, url)
                                    .PublishAsync(Mode.WaitForNone, cancellationToken);
                                _logger.LogDebug("Published URL citation notification: {Title} - {Url}", title, url);
                            }
                        }

                        if (!string.IsNullOrEmpty(chunk.SearchQuery))
                        {
                            await new DeepSearchQueryNotification(requestContext.ChatSession.Id, chunk.SearchQuery)
                                .PublishAsync(Mode.WaitForNone, cancellationToken);
                            _logger.LogDebug("Published search query notification: {Query}", chunk.SearchQuery);
                        }

                        if (!string.IsNullOrEmpty(chunk.FinishReason))
                        {
                            _logger.LogInformation("Stream turn {Turn} finished with reason: {FinishReason}", turn, chunk.FinishReason);
                            turnCompleted = true;
                            if (chunk.FinishReason == "tool_calls" || chunk.FinishReason == "function_call")
                            {
                                completedToolCalls = toolCallStates.Values
                                    .Where(s => !string.IsNullOrEmpty(s.Name) && s.ArgumentBuffer.Length > 0)
                                    .Select(s => new ParsedToolCall(s.Id!, s.Name!, s.ArgumentBuffer.ToString()))
                                    .ToList();
                            }
                            else
                            {
                                conversationCompleted = true;
                            }
                            break;
                        }

                    }

                    if (thinkingContentBuilder.Length > 0)
                    {
                        thinkingContent = thinkingContentBuilder.ToString();
                    }

                    if (cancellationToken.IsCancellationRequested) break;

                    if (allowToolCalls && completedToolCalls?.Any() == true)
                    {
                        aiMessage.UpdateContent(finalContent.ToString());
                        foreach (var call in completedToolCalls)
                        {
                            var resultMsg = await _toolCallHandler.ExecuteToolCallAsync(aiService, call, requestContext.ChatSession.Id, cancellationToken);
                            toolResults.Add(resultMsg);
                        }
                        toolRequestMsg = await _toolCallHandler.FormatAiMessageWithToolCallsAsync(modelType, completedToolCalls);
                    }
                    else if (turnCompleted)
                    {
                        conversationCompleted = true;
                        aiMessage.UpdateContent(finalContent.ToString());
                    }
                }
                finally
                {
                    if (_options.EnableObjectPooling)
                    {
                        ReturnToolCallStateDictionary(toolCallStates);
                        ReturnStringBuilder(thinkingContentBuilder);
                    }
                }
            }

            if (_options.EnableNotificationBatching && notificationBatch.Any())
            {
                idleFlushTimer?.Dispose();
                await FlushNotificationBatchAsync(notificationBatch, requestContext.ChatSession.Id, aiMessage.Id, cancellationToken);
            }
            else if (_options.EnableNotificationBatching)
            {
                idleFlushTimer?.Dispose();
            }

            if (!conversationCompleted && !cancellationToken.IsCancellationRequested)
            {
                if (finalContent.Length > 0)
                {
                    aiMessage.UpdateContent(finalContent.ToString());
                    aiMessage.InterruptMessage();
                    await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
                }
                if (!string.IsNullOrEmpty(thinkingContent))
                {
                    await UpdateMessageThinkingContentAsync(aiMessage, thinkingContent, CancellationToken.None);
                }
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                if (finalContent.Length > 0)
                {
                    aiMessage.UpdateContent(finalContent.ToString());
                }
                aiMessage.InterruptMessage();
                await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
                if (!string.IsNullOrEmpty(thinkingContent))
                {
                    await UpdateMessageThinkingContentAsync(aiMessage, thinkingContent, CancellationToken.None);
                }
            }
            else if (conversationCompleted)
            {
                if (finalContent.Length > 0)
                {
                    aiMessage.UpdateContent(finalContent.ToString());
                    await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
                }
                if (!string.IsNullOrEmpty(thinkingContent))
                {
                    await UpdateMessageThinkingContentAsync(aiMessage, thinkingContent, CancellationToken.None);
                }
            }

            return new StreamingResult(inTokens, outTokens, conversationCompleted, thinkingContent);
        }
        finally
        {
            if (_options.EnableObjectPooling)
            {
                ReturnStringBuilder(finalContent);
                ReturnMessageList(toolResults);
            }
        }
    }

    private bool ShouldFlushNotifications(List<(string Type, object Data)> batch, DateTime lastFlush)
    {
        return batch.Count >= _options.MaxChunkBatchSize || 
               (DateTime.UtcNow - lastFlush).TotalMilliseconds >= _options.NotificationBatchDelayMs;
    }

    private async Task FlushNotificationBatchAsync(
        List<(string Type, object Data)> batch, 
        Guid chatSessionId, 
        Guid messageId, 
        CancellationToken cancellationToken)
    {
        if (!batch.Any()) return;

        var textChunks = batch.Where(x => x.Type == "text").Select(x => (string)x.Data).ToList();
        var thinkingChunks = batch.Where(x => x.Type == "thinking").Select(x => (string)x.Data).ToList();

        try
        {
            if (textChunks.Any())
            {
                var combinedText = string.Join("", textChunks);
                await new MessageChunkReceivedNotification(chatSessionId, messageId, combinedText)
                    .PublishAsync(cancellation: cancellationToken);
            }

            if (thinkingChunks.Any())
            {
                var combinedThinking = string.Join("", thinkingChunks);
                await new ThinkingUpdateNotification(chatSessionId, messageId, combinedThinking)
                    .PublishAsync(cancellation: cancellationToken);
            }

        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Notification flush was canceled for message {MessageId}", messageId);
        }
    }

    private async Task<bool> TryFlushBatchAsync(
        List<(string Type, object Data)> batch,
        DateTime lastFlush,
        Guid chatSessionId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        if (ShouldFlushNotifications(batch, lastFlush))
        {
            await FlushNotificationBatchAsync(batch, chatSessionId, messageId, cancellationToken);
            batch.Clear();
            return true;
        }
        return false;
    }

    // Object pooling methods
    private StringBuilder RentStringBuilder()
    {
        if (_stringBuilderPool.TryDequeue(out var sb))
        {
            sb.Clear();
            return sb;
        }
        return new StringBuilder();
    }

    private void ReturnStringBuilder(StringBuilder sb)
    {
        if (sb != null && _stringBuilderPool.Count < _options.StringBuilderPoolSize)
        {
            _stringBuilderPool.Enqueue(sb);
        }
    }

    private List<MessageDto> RentMessageList()
    {
        if (_messageListPool.TryDequeue(out var list))
        {
            list.Clear();
            return list;
        }
        return new List<MessageDto>();
    }

    private void ReturnMessageList(List<MessageDto> list)
    {
        if (list != null && _messageListPool.Count < _options.MessageListPoolSize)
        {
            _messageListPool.Enqueue(list);
        }
    }

    private Dictionary<int, ToolCallState> RentToolCallStateDictionary()
    {
        if (_toolCallStatePool.TryDequeue(out var dict))
        {
            dict.Clear();
            return dict;
        }
        return new Dictionary<int, ToolCallState>();
    }

    private void ReturnToolCallStateDictionary(Dictionary<int, ToolCallState> dict)
    {
        if (dict != null && _toolCallStatePool.Count < _options.ToolCallStatePoolSize)
        {
            _toolCallStatePool.Enqueue(dict);
        }
    }

    private async Task UpdateMessageThinkingContentAsync(
        Message message,
        string? thinkingContent,
        CancellationToken cancellationToken = default)
    {
        message.UpdateThinkingContent(thinkingContent);
        await _messageRepository.UpdateAsync(message, cancellationToken);
    }

    private async Task PublishThinkingContentUpdatedAsync(Guid chatSessionId, Guid messageId, string thinkingContent)
    {
        try
        {
            await new ThinkingChunkReceivedNotification(chatSessionId, messageId, thinkingContent)
                .PublishAsync();

            _logger.LogDebug("Published thinking content update via SignalR for message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing thinking content update via SignalR for message {MessageId}", messageId);
        }
    }

    private class ToolCallState
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder ArgumentBuffer { get; } = new();
    }
}

public interface IStreamingService
{
    Task<StreamingResult> StreamResponseAsync(StreamingRequest request, CancellationToken cancellationToken);
} 