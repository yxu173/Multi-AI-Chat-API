using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Domain.Enums;
using Application.Services.Streaming;

namespace Application.Services;

public class MessageStreamer
{
    private readonly IMediator _mediator;
    private readonly IMessageRepository _messageRepository;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly ILogger<MessageStreamer> _logger;
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly StreamProcessor _streamProcessor;
    private readonly ToolCallHandler _toolCallHandler;
    private readonly TokenUsageTracker _tokenUsageTracker;

    public MessageStreamer(
        IMediator mediator,
        IMessageRepository messageRepository,
        StreamingOperationManager streamingOperationManager,
        ILogger<MessageStreamer> logger,
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler,
        TokenUsageTracker tokenUsageTracker)
    {
        _mediator = mediator;
        _messageRepository = messageRepository;
        _streamingOperationManager = streamingOperationManager;
        _logger = logger;
        _aiRequestHandler = aiRequestHandler;
        _streamProcessor = streamProcessor;
        _toolCallHandler = toolCallHandler;
        _tokenUsageTracker = tokenUsageTracker;
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
        string finalAiResponseContent = string.Empty;
        StringBuilder? finalAiResponseContentBuilder = null;
        MessageDto? aiMessageToolCallRequestMessage = null;
        List<MessageDto> toolResultMessages = new List<MessageDto>();
        List<MessageDto> originalHistory = new List<MessageDto>(requestContext.History);

        int maxTurns = 5;
        int turn = 0;
        bool aiResponseCompleted = false;

        try
        {
            // Group handling for non-streaming image models
            if (modelType == ModelType.AimlFlux || modelType == ModelType.Imagen)
            {
                await HandleSingleChunkImageResponse(requestContext, aiMessage, aiService, modelType, linkedToken);
            }
            else
            {
                await HandleStandardResponse(
                    requestContext,
                    aiMessage,
                    aiService,
                    modelType,
                    originalHistory,
                     totalInputTokens,
                     totalOutputTokens,
                     aiResponseCompleted,
                    linkedToken);
            }

            var finalUpdateToken = cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
            await FinalizeMessageState(aiMessage, linkedToken.IsCancellationRequested, finalUpdateToken);
            await _tokenUsageTracker.FinalizeTokenUsage(chatSessionId, aiModel, totalInputTokens, totalOutputTokens, finalUpdateToken);
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

    // Renamed from HandleAimlFluxResponse to handle multiple single-chunk image providers
    private async Task HandleSingleChunkImageResponse(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType, // Added modelType parameter for logging
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
                _logger.LogInformation("[{ModelType} Path] Received content, Length: {Length}", modelType, markdownResult.Length);
            }

            if (chunk.IsCompletion)
            {
                completedSuccessfully = !string.IsNullOrEmpty(markdownResult);
                _logger.LogInformation("[{ModelType} Path] Completion chunk received. Success: {Success}", modelType, completedSuccessfully);
                break;
            }
            else
            {
                _logger.LogWarning("[{ModelType} Path] Received non-completion chunk for message {MessageId}. Content Length: {Length}", modelType, aiMessage.Id, chunk.RawContent?.Length ?? 0);
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
            await _mediator.Publish(new MessageChunkReceivedNotification(requestContext.ChatSession.Id, aiMessage.Id, markdownResult), cancellationToken);
        }
        else
        {
            aiMessage.AppendContent($"\n[Failed to get valid image response from {modelType}]");
            aiMessage.FailMessage();
            _logger.LogWarning("{ModelType} response processing failed or resulted in empty/invalid content for message {MessageId}", modelType, aiMessage.Id);
        }
    }

    private async Task HandleStandardResponse(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType,
        List<MessageDto> originalHistory,
         int totalInputTokens,
         int totalOutputTokens,
         bool aiResponseCompleted,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using standard streaming response path for model {ModelType}", modelType);
        var finalAiResponseContentBuilder = new StringBuilder();
        var toolResultMessages = new List<MessageDto>();
        MessageDto? aiMessageToolCallRequestMessage = null;
        int turn = 0;
        int maxTurns = 5;

        while (turn < maxTurns && !aiResponseCompleted && !cancellationToken.IsCancellationRequested)
        {
            turn++;
            _logger.LogInformation("Starting AI interaction turn {Turn} for Message {MessageId}", turn, aiMessage.Id);

            var historyForThisTurn = new List<MessageDto>(originalHistory);
            if (aiMessageToolCallRequestMessage != null)
            {
                historyForThisTurn.Add(aiMessageToolCallRequestMessage);
                historyForThisTurn.AddRange(toolResultMessages);
                _logger.LogDebug("Added previous turn's tool request and {ResultCount} results to history for Turn {Turn}", toolResultMessages.Count, turn);
            }

            var currentRequestContext = requestContext with { History = historyForThisTurn };
            var requestPayload = await _aiRequestHandler.PrepareRequestPayloadAsync(currentRequestContext, cancellationToken);

            _logger.LogInformation("Initiating AI stream request (Turn {Turn}) for message {MessageId}", turn, aiMessage.Id);
            var rawStream = aiService.StreamResponseAsync(requestPayload, cancellationToken);

            toolResultMessages.Clear();
            aiMessageToolCallRequestMessage = null;

            var streamResult = await _streamProcessor.ProcessStreamAsync(
                rawStream,
                modelType,
                requestContext.SpecificModel.SupportsThinking,
                aiMessage,
                requestContext.ChatSession.Id,
                (textChunk) => finalAiResponseContentBuilder.Append(textChunk),
                cancellationToken);

            totalInputTokens += streamResult.InputTokens;
            totalOutputTokens += streamResult.OutputTokens;

            if (cancellationToken.IsCancellationRequested) break;

            if (streamResult.ToolCalls?.Any() == true)
            {
                _logger.LogInformation("AI requested {ToolCallCount} tool calls (Turn {Turn})", streamResult.ToolCalls.Count, turn);

                foreach (var toolCall in streamResult.ToolCalls)
                {
                    var toolResult = await _toolCallHandler.ExecuteToolCallAsync(toolCall, modelType, aiMessage, cancellationToken);
                    toolResultMessages.Add(toolResult);
                }

                aiMessageToolCallRequestMessage = await _toolCallHandler.FormatAiMessageWithToolCallsAsync(modelType, streamResult.ToolCalls);
            }
            else if (streamResult.IsComplete)
            {
                _logger.LogInformation("AI stream completed without tool calls (Turn {Turn})", turn);
                aiResponseCompleted = true;
            }
            else
            {
                _logger.LogWarning("AI stream finished unexpectedly without completion or tool call (Turn {Turn})", turn);
                aiResponseCompleted = true;
            }
        }

        _logger.LogInformation("AI interaction loop finished for Message {MessageId}. AI Response Completed: {IsCompleted}", aiMessage.Id, aiResponseCompleted);

        string finalContent = finalAiResponseContentBuilder.ToString();
        if (!string.IsNullOrEmpty(finalContent))
        {
            aiMessage.UpdateContent(finalContent);
            _logger.LogInformation("Final AI message content updated (Length: {Length})", finalContent.Length);
        }
        else if (!aiResponseCompleted && !cancellationToken.IsCancellationRequested)
        {
            aiMessage.AppendContent("\n[AI response incomplete or ended unexpectedly]");
            _logger.LogWarning("AI response seems incomplete for message {MessageId} after loop.", aiMessage.Id);
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
                _logger.LogWarning("Message {MessageId} status was still Streaming before final save. Setting to Interrupted.", aiMessage.Id);
                aiMessage.InterruptMessage();
            }
        }

        await _messageRepository.UpdateAsync(aiMessage, cancellationToken);
        _logger.LogInformation("Saved final state for message {MessageId} with status {Status}", aiMessage.Id, aiMessage.Status);

        if (aiMessage.Status == MessageStatus.Completed)
        {
            await _mediator.Publish(new ResponseCompletedNotification(aiMessage.ChatSessionId, aiMessage.Id), cancellationToken);
        }
        else if (aiMessage.Status == MessageStatus.Interrupted && !cancellationToken.IsCancellationRequested)
        {
            await _mediator.Publish(new ResponseStoppedNotification(aiMessage.ChatSessionId, aiMessage.Id), cancellationToken);
        }
        else if (aiMessage.Status == MessageStatus.Failed)
        {
            await _mediator.Publish(new ResponseStoppedNotification(aiMessage.ChatSessionId, aiMessage.Id), cancellationToken);
        }
    }

    private async Task HandleCancellation(Guid chatSessionId, Message aiMessage, bool userCancelled)
    {
        _logger.LogInformation("Streaming operation cancelled for chat session {ChatSessionId}. Reason: {Reason}",
            chatSessionId, userCancelled ? "User Request" : "Internal Stop");

        var stopReason = userCancelled ? "Cancelled by user" : "Stopped internally";
        await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), CancellationToken.None);

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
        if (aiMessage.Status != MessageStatus.Completed && aiMessage.Status != MessageStatus.Interrupted && aiMessage.Status != MessageStatus.Failed)
        {
            aiMessage.AppendContent($"\n[Error: {ex.Message}]");
            aiMessage.FailMessage();
            await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
        }
        await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), CancellationToken.None);
    }
}