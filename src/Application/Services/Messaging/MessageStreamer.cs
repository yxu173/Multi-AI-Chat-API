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
    private readonly IMessageRepository _messageRepository;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly ILogger<MessageStreamer> _logger;
    private readonly TokenUsageService _tokenUsageService;
    private readonly MessageService _messageService;
    private readonly Dictionary<ResponseType, IResponseHandler> _responseHandlers;

    public MessageStreamer(
        IMessageRepository messageRepository,
        StreamingOperationManager streamingOperationManager,
        ILogger<MessageStreamer> logger,
        TokenUsageService tokenUsageService,
        MessageService messageService,
        IEnumerable<IResponseHandler> responseHandlers
    )
    {
        _messageRepository = messageRepository;
        _streamingOperationManager = streamingOperationManager;
        _logger = logger;
        _tokenUsageService = tokenUsageService;
        _messageService = messageService;
        _responseHandlers = responseHandlers.ToDictionary(h => h.ResponseType);
    }

    /// <summary>
    /// Handles the end-to-end process of streaming an AI response for a given chat context.
    /// </summary>
    public async Task StreamResponseAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        CancellationToken cancellationToken)
    {
        var chatSessionId = requestContext.ChatSession.Id;
        var aiModel = requestContext.SpecificModel;
        var modelType = aiModel.ModelType;
        var userId = requestContext.UserId;

        var cts = new CancellationTokenSource();
        _streamingOperationManager.RegisterOperation(aiMessage.Id, cts);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var linkedToken = linkedCts.Token;

        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        List<MessageDto> originalHistory = new List<MessageDto>(requestContext.History);

        bool aiResponseCompleted = false;

        try
        {
            var responseType = MapModelTypeToResponse(modelType);

            if (!_responseHandlers.TryGetValue(responseType, out var handler))
            {
                throw new InvalidOperationException($"No response handler registered for {responseType}");
            }

            var handlerResult = await handler.HandleAsync(requestContext, aiMessage, aiService, modelType, linkedToken);

            totalInputTokens = handlerResult.TotalInputTokens;
            totalOutputTokens = handlerResult.TotalOutputTokens;
            aiResponseCompleted = handlerResult.AiResponseCompleted;

            if (!string.IsNullOrEmpty(handlerResult.AccumulatedThinkingContent))
            {
                await _messageService.UpdateMessageThinkingContentAsync(aiMessage,
                    handlerResult.AccumulatedThinkingContent,
                    CancellationToken.None);
                //TODO: SignalR stream here
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

            var finalUpdateToken =
                cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
            await FinalizeMessageState(aiMessage, aiResponseCompleted, finalUpdateToken);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            await HandleCancellation(chatSessionId, aiMessage, cancellationToken.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            await HandleError(chatSessionId, aiMessage, ex);
        }
        finally
        {
            _streamingOperationManager.StopStreaming(aiMessage.Id);
            linkedCts.Dispose();
            cts.Dispose();
            _logger.LogInformation("Streaming operation finished or cleaned up for message {MessageId}", aiMessage.Id);
        }
    }

    private async Task FinalizeMessageState(Message aiMessage, bool wasCancelled, CancellationToken cancellationToken)
    {
        if (aiMessage.Status == MessageStatus.Streaming)
        {
            if (wasCancelled)
            {
                aiMessage.InterruptMessage();
            }
            else
            {
                aiMessage.CompleteMessage();
            }
        }

        await _messageRepository.UpdateAsync(aiMessage, cancellationToken);
        _logger.LogInformation("Saved final state for message {MessageId} with status {Status}", aiMessage.Id,
            aiMessage.Status);

        if (aiMessage.Status == MessageStatus.Completed)
        {
            await new ResponseCompletedNotification(aiMessage.ChatSessionId, aiMessage.Id).PublishAsync(cancellation: cancellationToken);
        }
        else if (aiMessage.Status == MessageStatus.Interrupted && !cancellationToken.IsCancellationRequested)
        {
            await new ResponseStoppedNotification(aiMessage.ChatSessionId, aiMessage.Id).PublishAsync(cancellation: cancellationToken);
        }
        else if (aiMessage.Status == MessageStatus.Failed)
        {
            await new ResponseStoppedNotification(aiMessage.ChatSessionId, aiMessage.Id).PublishAsync(cancellation: cancellationToken);
        }
    }

    private async Task HandleCancellation(Guid chatSessionId, Message aiMessage, bool userCancelled)
    {
        _logger.LogInformation("Streaming operation cancelled for chat session {ChatSessionId}. Reason: {Reason}",
            chatSessionId, userCancelled ? "User Request" : "Internal Stop");

        var stopReason = userCancelled ? "Cancelled by user" : "Stopped internally";
        await new ResponseStoppedNotification(chatSessionId, aiMessage.Id).PublishAsync();

        if (!aiMessage.IsTerminal())
        {
            aiMessage.AppendContent($"\n[{stopReason}]");
            aiMessage.InterruptMessage();
            await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
        }
    }

    private async Task HandleError(Guid chatSessionId, Message aiMessage, Exception ex)
    {
        _logger.LogError(ex, "Error during AI response streaming for chat session {ChatSessionId}.", chatSessionId);
        if (!aiMessage.IsTerminal())
        {
            aiMessage.AppendContent($"\n[Error: {ex.Message}]");
            aiMessage.FailMessage();
            await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
        }

        await new ResponseStoppedNotification(chatSessionId, aiMessage.Id).PublishAsync();
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
}