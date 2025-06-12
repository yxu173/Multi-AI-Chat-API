using Application.Abstractions.Interfaces;
using Application.Exceptions;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Helpers;
using Application.Services.Infrastructure;
using Application.Services.Messaging;
using Application.Services.TokenUsage;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.Services.Streaming;

public record StreamingRequest(
    Guid ChatSessionId,
    Guid UserId,
    Guid AiMessageId,
    bool EnableThinking = false,
    string? ImageSize = null,
    int? NumImages = null,
    string? OutputFormat = null,
    bool? EnableSafetyChecker = null,
    string? SafetyTolerance = null
);

public record StreamingResult(
    int InputTokens,
    int OutputTokens,
    bool ConversationCompleted,
    string? AccumulatedThinkingContent
);

public class StreamingService : IStreamingService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IProviderKeyManagementService _providerKeyManagementService;
    private readonly ILogger<StreamingService> _logger;
    private readonly IUserAiModelSettingsRepository _userAiModelSettingsRepository;
    private readonly IAiAgentRepository _aiAgentRepository;
    private readonly IToolDefinitionService _toolDefinitionService;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly TokenUsageService _tokenUsageService;
    private readonly IMessageRepository _messageRepository;
    private readonly IAiMessageFinalizer _aiMessageFinalizer;
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly ToolCallHandler _toolCallHandler;

    private const int MaxRetries = 3;
    private readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private const double RetryBackoffFactor = 2.0;

    private static readonly ActivitySource ActivitySource = new("Application.Services.Streaming.StreamingService", "1.0.0");

    public StreamingService(
        IChatSessionRepository chatSessionRepository,
        ISubscriptionService subscriptionService,
        IAiModelServiceFactory aiModelServiceFactory,
        IProviderKeyManagementService providerKeyManagementService,
        ILogger<StreamingService> logger,
        IUserAiModelSettingsRepository userAiModelSettingsRepository,
        IAiAgentRepository aiAgentRepository,
        IToolDefinitionService toolDefinitionService,
        StreamingOperationManager streamingOperationManager,
        TokenUsageService tokenUsageService,
        IMessageRepository messageRepository,
        IAiMessageFinalizer aiMessageFinalizer,
        IAiRequestHandler aiRequestHandler,
        ToolCallHandler toolCallHandler)
    {
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
        _providerKeyManagementService = providerKeyManagementService ?? throw new ArgumentNullException(nameof(providerKeyManagementService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userAiModelSettingsRepository = userAiModelSettingsRepository ?? throw new ArgumentNullException(nameof(userAiModelSettingsRepository));
        _aiAgentRepository = aiAgentRepository ?? throw new ArgumentNullException(nameof(aiAgentRepository));
        _toolDefinitionService = toolDefinitionService ?? throw new ArgumentNullException(nameof(toolDefinitionService));
        _streamingOperationManager = streamingOperationManager ?? throw new ArgumentNullException(nameof(streamingOperationManager));
        _tokenUsageService = tokenUsageService ?? throw new ArgumentNullException(nameof(tokenUsageService));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _aiMessageFinalizer = aiMessageFinalizer ?? throw new ArgumentNullException(nameof(aiMessageFinalizer));
        _aiRequestHandler = aiRequestHandler ?? throw new ArgumentNullException(nameof(aiRequestHandler));
        _toolCallHandler = toolCallHandler ?? throw new ArgumentNullException(nameof(toolCallHandler));
    }

    public async Task<StreamingResult> StreamResponseAsync(StreamingRequest request, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(nameof(StreamingService.StreamResponseAsync));
        activity?.SetTag("chat_session.id", request.ChatSessionId.ToString());
        activity?.SetTag("user.id", request.UserId.ToString());
        activity?.SetTag("ai_message.id", request.AiMessageId.ToString());

        var chatSession = await _chatSessionRepository.GetByIdWithMessagesAndModelAndProviderAsync(request.ChatSessionId)
            ?? throw new NotFoundException(nameof(ChatSession), request.ChatSessionId);

        var aiMessage = chatSession.Messages.FirstOrDefault(m => m.Id == request.AiMessageId)
            ?? throw new NotFoundException(nameof(Message), request.AiMessageId);

        var (hasQuota, errorMessage) = await _subscriptionService.CheckUserQuotaAsync(request.UserId, 1, 0, cancellationToken);
        if (!hasQuota)
        {
            throw new QuotaExceededException(errorMessage ?? "User has exceeded their quota.");
        }

        var userSettings = await _userAiModelSettingsRepository.GetDefaultByUserIdAsync(request.UserId, cancellationToken);
        var aiAgent = chatSession.AiAgentId.HasValue
            ? await _aiAgentRepository.GetByIdAsync(chatSession.AiAgentId.Value, cancellationToken)
            : null;

        var toolDefinitions = await _toolDefinitionService.GetToolDefinitionsAsync(request.UserId, cancellationToken);

        var requestContext = new AiRequestContext(
            UserId: request.UserId,
            ChatSession: chatSession,
            History: HistoryBuilder.BuildHistory(chatSession, aiAgent, userSettings, MessageDto.FromEntity(aiMessage)),
            AiAgent: aiAgent,
            UserSettings: userSettings,
            SpecificModel: chatSession.AiModel,
            RequestSpecificThinking: request.EnableThinking,
            ImageSize: request.ImageSize,
            NumImages: request.NumImages,
            OutputFormat: request.OutputFormat,
            EnableSafetyChecker: request.EnableSafetyChecker,
            SafetyTolerance: request.SafetyTolerance,
            ToolDefinitions: toolDefinitions);

        var cts = new CancellationTokenSource();
        _streamingOperationManager.RegisterOperation(aiMessage.Id, cts);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var linkedToken = linkedCts.Token;

        try
        {
            var result = await ProcessStreamingWithRetriesAsync(requestContext, aiMessage, linkedToken);
            
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

    private async Task<StreamingResult> ProcessStreamingWithRetriesAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            TimeSpan delayForThisAttempt = TimeSpan.FromSeconds(Math.Pow(RetryBackoffFactor, attempt - 1) * InitialRetryDelay.TotalSeconds);

            try
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

                _logger.LogInformation("Successfully streamed AI response for chat {ChatSessionId} on attempt {Attempt}", 
                    requestContext.ChatSession.Id, attempt);
                return result;
            }
            catch (ProviderRateLimitException ex)
            {
                _logger.LogWarning(ex, "Provider rate limit hit on attempt {Attempt} for chat {ChatSessionId}. Key ID: {ApiKeyIdUsed}", 
                    attempt, requestContext.ChatSession.Id, ex.ApiKeyIdUsed);
                if (ex.ApiKeyIdUsed.HasValue)
                {
                    await _providerKeyManagementService.ReportKeyRateLimitedAsync(ex.ApiKeyIdUsed.Value, ex.RetryAfter ?? delayForThisAttempt, CancellationToken.None);
                }
                if (attempt == MaxRetries) throw;
                var actualDelay = ex.RetryAfter ?? delayForThisAttempt;
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to provider rate limit.", actualDelay, requestContext.ChatSession.Id);
                await Task.Delay(actualDelay, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request exception on attempt {Attempt} for chat {ChatSessionId}", attempt, requestContext.ChatSession.Id);
                if (attempt == MaxRetries) throw;
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to HTTP request exception.", delayForThisAttempt, requestContext.ChatSession.Id);
                await Task.Delay(delayForThisAttempt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on attempt {Attempt} during AI interaction for chat {ChatSessionId}", attempt, requestContext.ChatSession.Id);
                if (attempt == MaxRetries) throw;
                _logger.LogInformation("Retrying after {Delay} for chat {ChatSessionId} due to unhandled exception.", delayForThisAttempt, requestContext.ChatSession.Id);
                await Task.Delay(delayForThisAttempt, cancellationToken);
            }
        }

        throw new Exception($"Failed to get AI response for chat {requestContext.ChatSession.Id} after {MaxRetries} attempts. Please try again later.");
    }

    private async Task<StreamingResult> ProcessConversationTurnAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        var finalContent = new System.Text.StringBuilder();
        var toolResults = new List<MessageDto>();
        MessageDto? toolRequestMsg = null;
        string? thinkingContent = null;
        int inTokens = 0;
        int outTokens = 0;
        bool conversationCompleted = false;
        int turn = 0;
        const int MaxTurns = 5;

        var baseHistory = new List<MessageDto>(requestContext.History);
        var modelType = requestContext.SpecificModel.ModelType;
        var allowToolCalls = modelType == ModelType.OpenAi || modelType == ModelType.Anthropic || 
                           modelType == ModelType.Gemini || modelType == ModelType.DeepSeek || 
                           modelType == ModelType.Grok;

        while (turn < MaxTurns && !conversationCompleted && !cancellationToken.IsCancellationRequested)
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
            var stream = aiService.StreamResponseAsync(payload, cancellationToken, providerApiKeyId);

            toolResults.Clear();
            toolRequestMsg = null;
            finalContent.Clear();

            var toolCallStates = new Dictionary<int, ToolCallState>();
            var thinkingContentBuilder = new System.Text.StringBuilder();
            List<ParsedToolCall>? completedToolCalls = null;
            bool turnCompleted = false;

            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                if (chunk.InputTokens.HasValue) inTokens = chunk.InputTokens.Value;
                if (chunk.OutputTokens.HasValue) outTokens = chunk.OutputTokens.Value;

                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    finalContent.Append(chunk.TextDelta);
                    await new MessageChunkReceivedNotification(requestContext.ChatSession.Id, aiMessage.Id, chunk.TextDelta).PublishAsync(cancellation: cancellationToken);
                }

                bool supportsThinking = requestContext.RequestSpecificThinking ?? requestContext.ChatSession.EnableThinking;
                if (supportsThinking && !string.IsNullOrEmpty(chunk.ThinkingDelta))
                {
                    thinkingContentBuilder.Append(chunk.ThinkingDelta);
                    await new ThinkingUpdateNotification(requestContext.ChatSession.Id, aiMessage.Id, chunk.ThinkingDelta).PublishAsync(cancellation: cancellationToken);
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
                    var resultMsg = await _toolCallHandler.ExecuteToolCallAsync(aiService, call, cancellationToken);
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

        if (!conversationCompleted && !cancellationToken.IsCancellationRequested)
        {
            aiMessage.UpdateContent(finalContent.ToString());
            aiMessage.InterruptMessage();
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            aiMessage.InterruptMessage();
        }

        return new StreamingResult(inTokens, outTokens, conversationCompleted, thinkingContent);
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
        public System.Text.StringBuilder ArgumentBuffer { get; } = new System.Text.StringBuilder();
    }
}

public interface IStreamingService
{
    Task<StreamingResult> StreamResponseAsync(StreamingRequest request, CancellationToken cancellationToken);
} 