using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.Infrastructure;
using Application.Services.TokenUsage;
using Application.Services.Utilities;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;

namespace Application.Services.Messaging;

public class MessageStreamer : IMessageStreamer
{
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly ILogger<MessageStreamer> _logger;
    private readonly TokenUsageService _tokenUsageService;
    private readonly IMessageRepository _messageRepository;
    private readonly Dictionary<ResponseType, IResponseHandler> _responseHandlers;
    private readonly IAiMessageFinalizer _aiMessageFinalizer;

    public MessageStreamer(
        StreamingOperationManager streamingOperationManager,
        ILogger<MessageStreamer> logger,
        TokenUsageService tokenUsageService,
        IMessageRepository messageRepository,
        IEnumerable<IResponseHandler> responseHandlers,
        IAiMessageFinalizer aiMessageFinalizer
    )
    {
        _streamingOperationManager = streamingOperationManager;
        _logger = logger;
        _tokenUsageService = tokenUsageService;
        _messageRepository = messageRepository;
        _responseHandlers = responseHandlers.ToDictionary(h => h.ResponseType);
        _aiMessageFinalizer = aiMessageFinalizer;
    }

    /// <summary>
    /// Handles the end-to-end process of streaming an AI response for a given chat context.
    /// </summary>
    public async Task StreamResponseAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        var chatSessionId = requestContext.ChatSession.Id;
        var aiModel = requestContext.SpecificModel;
        var modelType = aiModel.ModelType;

        var cts = new CancellationTokenSource();
        _streamingOperationManager.RegisterOperation(aiMessage.Id, cts);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var linkedToken = linkedCts.Token;

        try
        {
            var responseType = MapModelTypeToResponse(modelType);

            if (!_responseHandlers.TryGetValue(responseType, out var handler))
            {
                throw new InvalidOperationException($"No response handler registered for {responseType}");
            }

            var handlerResult = await handler.HandleAsync(requestContext, aiMessage, aiService, modelType, linkedToken, providerApiKeyId);

            var totalInputTokens = handlerResult.TotalInputTokens;
            var totalOutputTokens = handlerResult.TotalOutputTokens;
            var aiResponseCompletedSuccessfully = handlerResult.AiResponseCompleted;

            if (!string.IsNullOrEmpty(handlerResult.AccumulatedThinkingContent))
            {
                await UpdateMessageThinkingContentAsync(aiMessage,
                    handlerResult.AccumulatedThinkingContent,
                    CancellationToken.None);
                
                await PublishThinkingContentUpdatedAsync(requestContext.ChatSession.Id, aiMessage.Id, 
                    handlerResult.AccumulatedThinkingContent);
            }

            if (totalInputTokens > 0 || totalOutputTokens > 0)
            {
                decimal finalCost = aiModel.CalculateCost(totalInputTokens, totalOutputTokens);
                _logger.LogInformation(
                    "Updating final accumulated token usage for ChatSession {ChatSessionId}: Input={InputTokens}, Output={OutputTokens}, Cost={Cost}",
                    chatSessionId, totalInputTokens, totalOutputTokens, finalCost);
                await _tokenUsageService.UpdateTokenUsageAsync(chatSessionId, totalInputTokens, totalOutputTokens,
                    finalCost, CancellationToken.None);
            }

            var persistenceToken = cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
            await _aiMessageFinalizer.FinalizeProgressingMessageAsync(aiMessage, aiResponseCompletedSuccessfully, persistenceToken);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            bool wasDirectUserCancellation = cancellationToken.IsCancellationRequested;
            await _aiMessageFinalizer.FinalizeAfterCancellationAsync(aiMessage, wasDirectUserCancellation, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _aiMessageFinalizer.FinalizeAfterErrorAsync(aiMessage, ex, CancellationToken.None);
        }
        finally
        {
            _streamingOperationManager.StopStreaming(aiMessage.Id);
            linkedCts.Dispose();
            cts.Dispose();
            _logger.LogInformation("Streaming operation finished or cleaned up for message {MessageId}", aiMessage.Id);
        }
    }

    private ResponseType MapModelTypeToResponse(ModelType modelType) =>
        modelType switch
        {
            ModelType.AimlFlux or ModelType.Imagen => ResponseType.Image,
            ModelType.OpenAi or ModelType.Anthropic
                or ModelType.Gemini or ModelType.DeepSeek
                or ModelType.Grok => ResponseType.ToolCall,
            _ => ResponseType.Text
        };
    
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

    private async Task UpdateMessageThinkingContentAsync(
        Message message,
        string? thinkingContent,
        CancellationToken cancellationToken = default)
    {
        message.UpdateThinkingContent(thinkingContent);
        await _messageRepository.UpdateAsync(message, cancellationToken);
    }
}