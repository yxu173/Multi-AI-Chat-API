using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Application.Services.Infrastructure;
using Application.Services.TokenUsage;
using Application.Services.Utilities;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using FastEndpoints;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Services.Messaging;

public class MessageStreamer
{
    private readonly IMediator _mediator;
    private readonly IMessageRepository _messageRepository;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly ILogger<MessageStreamer> _logger;
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly StreamProcessor _streamProcessor;
    private readonly ToolCallHandler _toolCallHandler;
    private readonly TokenUsageService _tokenUsageService;
    private readonly MessageService _messageService;

    public MessageStreamer(
        IMediator mediator,
        IMessageRepository messageRepository,
        StreamingOperationManager streamingOperationManager,
        ILogger<MessageStreamer> logger,
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler,
        TokenUsageService tokenUsageService,
        MessageService messageService
    )
    {
        _mediator = mediator;
        _messageRepository = messageRepository;
        _streamingOperationManager = streamingOperationManager;
        _logger = logger;
        _aiRequestHandler = aiRequestHandler;
        _streamProcessor = streamProcessor;
        _toolCallHandler = toolCallHandler;
        _tokenUsageService = tokenUsageService;
        _messageService = messageService;
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
            switch (responseType)
            {
                case ResponseType.Image:
                    await HandleSingleChunkImageResponse(requestContext, aiMessage, aiService, modelType, linkedToken);
                    aiResponseCompleted = true;
                    break;
                default:
                    var standardResponseResult = await HandleStandardResponse(
                        requestContext,
                        aiMessage,
                        aiService,
                        modelType,
                        originalHistory,
                        linkedToken);

                    totalInputTokens = standardResponseResult.TotalInputTokens;
                    totalOutputTokens = standardResponseResult.TotalOutputTokens;
                    aiResponseCompleted = standardResponseResult.AiResponseCompleted;

                    if (!string.IsNullOrEmpty(standardResponseResult.AccumulatedThinkingContent))
                    {
                        await _messageService.UpdateMessageThinkingContentAsync(aiMessage,
                            standardResponseResult.AccumulatedThinkingContent,
                            CancellationToken.None);
                        
                        //TODO: SignalR stream here
                    }

                    decimal finalCost = aiModel.CalculateCost(totalInputTokens, totalOutputTokens);
                    _logger.LogInformation(
                        "Updating final accumulated token usage for ChatSession {ChatSessionId}: Input={InputTokens}, Output={OutputTokens}, Cost={Cost}",
                        chatSessionId, totalInputTokens, totalOutputTokens, finalCost);
                    await _tokenUsageService.UpdateTokenUsageAsync(chatSessionId, totalInputTokens, totalOutputTokens,
                        finalCost, CancellationToken.None);
                    break;
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

    private async Task HandleSingleChunkImageResponse(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using single-chunk response path for model {ModelType}", modelType);

        var requestPayload = await _aiRequestHandler.PrepareRequestPayloadAsync(requestContext, cancellationToken);
        string? markdownResult = null;
        bool completedSuccessfully = false;

        await foreach (var chunk in aiService.StreamResponseAsync(requestPayload, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!string.IsNullOrEmpty(chunk.RawContent))
            {
                markdownResult = chunk.RawContent;
                _logger.LogInformation("[{ModelType} Path] Received content, Length: {Length}", modelType,
                    markdownResult.Length);
            }

            if (chunk.IsCompletion)
            {
                completedSuccessfully = !string.IsNullOrEmpty(markdownResult);
                _logger.LogInformation("[{ModelType} Path] Completion chunk received. Success: {Success}", modelType,
                    completedSuccessfully);
                break;
            }
            else
            {
                _logger.LogWarning(
                    "[{ModelType} Path] Received non-completion chunk for message {MessageId}. Content Length: {Length}",
                    modelType, aiMessage.Id, chunk.RawContent?.Length ?? 0);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            aiMessage.AppendContent($"\n[Cancelled]");
            aiMessage.InterruptMessage();
        }
        else if (completedSuccessfully && markdownResult != null)
        {
            aiMessage.UpdateContent(markdownResult);
            aiMessage.CompleteMessage();
            await new MessageChunkReceivedNotification(requestContext.ChatSession.Id, aiMessage.Id, markdownResult).PublishAsync(cancellation: cancellationToken);
        }
        else
        {
            aiMessage.AppendContent($"\n[Failed to get valid image response from {modelType}]");
            aiMessage.FailMessage();
            _logger.LogWarning(
                "{ModelType} response processing failed or resulted in empty/invalid content for message {MessageId}",
                modelType, aiMessage.Id);
        }
    }

    private record StandardResponseResult(
        int TotalInputTokens,
        int TotalOutputTokens,
        bool AiResponseCompleted,
        string? AccumulatedThinkingContent);

    private async Task<StandardResponseResult> HandleStandardResponse(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType,
        List<MessageDto> originalHistory,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using standard streaming response path for model {ModelType}", modelType);
        var finalAiResponseContentBuilder = new StringBuilder();
        var toolResultMessages = new List<MessageDto>();
        MessageDto? aiMessageToolCallRequestMessage = null;
        string? finalAccumulatedThinkingContent = null;
        int turn = 0;
        int maxTurns = 5;
        int accumulatedInputTokens = 0;
        int accumulatedOutputTokens = 0;
        bool finalAiResponseCompleted = false;

        while (turn < maxTurns && !finalAiResponseCompleted && !cancellationToken.IsCancellationRequested)
        {
            turn++;
            _logger.LogInformation("Starting AI interaction turn {Turn} for Message {MessageId}", turn, aiMessage.Id);

            var historyForThisTurn = new List<MessageDto>(originalHistory);
            if (aiMessageToolCallRequestMessage != null)
            {
                historyForThisTurn.Add(aiMessageToolCallRequestMessage);
                historyForThisTurn.AddRange(toolResultMessages);
                _logger.LogDebug(
                    "Added previous turn's tool request and {ResultCount} results to history for Turn {Turn}",
                    toolResultMessages.Count, turn);
            }

            var currentRequestContext = requestContext with { History = historyForThisTurn };
            var requestPayload =
                await _aiRequestHandler.PrepareRequestPayloadAsync(currentRequestContext, cancellationToken);

            _logger.LogInformation("Initiating AI stream request (Turn {Turn}) for message {MessageId}", turn,
                aiMessage.Id);
            var rawStream = aiService.StreamResponseAsync(requestPayload, cancellationToken);

            toolResultMessages.Clear();
            aiMessageToolCallRequestMessage = null;

            var streamResult = await _streamProcessor.ProcessStreamAsync(
                rawStream,
                modelType,
                requestContext.SpecificModel.SupportsThinking || requestContext.ChatSession.EnableThinking,
                aiMessage,
                requestContext.ChatSession.Id,
                (textChunk) => finalAiResponseContentBuilder.Append(textChunk),
                cancellationToken);

            if (!string.IsNullOrEmpty(streamResult.ThinkingContent))
            {
                finalAccumulatedThinkingContent = streamResult.ThinkingContent;
                _logger.LogDebug("Captured thinking content in Turn {Turn} for message {MessageId} (Length: {Length})",
                    turn, aiMessage.Id, finalAccumulatedThinkingContent.Length);
            }

            accumulatedInputTokens += streamResult.InputTokens;
            accumulatedOutputTokens += streamResult.OutputTokens;

            if (cancellationToken.IsCancellationRequested) break;

            if (streamResult.ToolCalls?.Any() == true)
            {
                _logger.LogInformation("AI requested {ToolCallCount} tool calls (Turn {Turn})",
                    streamResult.ToolCalls.Count, turn);

                aiMessage.UpdateContent(finalAiResponseContentBuilder.ToString());

                foreach (var toolCall in streamResult.ToolCalls)
                {
                    var toolResult =
                        await _toolCallHandler.ExecuteToolCallAsync(toolCall, modelType, aiMessage, cancellationToken);
                    toolResultMessages.Add(toolResult);
                }

                aiMessageToolCallRequestMessage =
                    await _toolCallHandler.FormatAiMessageWithToolCallsAsync(modelType, streamResult.ToolCalls);

                finalAiResponseContentBuilder.Clear();
            }
            else if (streamResult.IsComplete)
            {
                _logger.LogInformation("AI stream completed without tool calls (Turn {Turn})", turn);
                finalAiResponseCompleted = true;
                aiMessage.UpdateContent(finalAiResponseContentBuilder.ToString()); // Ensure final content is set
            }
            else
            {
                _logger.LogWarning("Stream processing finished turn {Turn} unexpectedly.", turn);
            }
        }

        if (!finalAiResponseCompleted && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "AI interaction loop finished after {MaxTurns} turns without full completion for message {MessageId}",
                maxTurns, aiMessage.Id);
            aiMessage.UpdateContent(finalAiResponseContentBuilder.ToString());
            aiMessage.InterruptMessage();
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("AI interaction cancelled during turn {Turn} for message {MessageId}", turn,
                aiMessage.Id);
            aiMessage.InterruptMessage();
        }


        return new StandardResponseResult(accumulatedInputTokens, accumulatedOutputTokens, finalAiResponseCompleted,
            finalAccumulatedThinkingContent);
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

        if (aiMessage.Status != MessageStatus.Completed && aiMessage.Status != MessageStatus.Interrupted)
        {
            aiMessage.AppendContent($"\n[{stopReason}]");
            aiMessage.InterruptMessage();
            await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
        }
    }

    private async Task HandleError(Guid chatSessionId, Message aiMessage, Exception ex)
    {
        _logger.LogError(ex, "Error during AI response streaming for chat session {ChatSessionId}.", chatSessionId);
        if (aiMessage.Status != MessageStatus.Completed && aiMessage.Status != MessageStatus.Interrupted &&
            aiMessage.Status != MessageStatus.Failed)
        {
            aiMessage.AppendContent($"\n[Error: {ex.Message}]");
            aiMessage.FailMessage();
            await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
        }

        await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), CancellationToken.None);
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