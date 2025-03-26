using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;

namespace Application.Services;

public class MessageStreamer
{
    private readonly IMessageRepository _messageRepository;
    private readonly TokenUsageService _tokenUsageService;
    private readonly IMediator _mediator;
    private readonly StreamingOperationManager _streamingOperationManager;

    // Variables to accumulate token usage
    private int _totalInputTokens = 0;
    private int _totalOutputTokens = 0;
    private bool _inputTokensAdded = false;
    private int _lastOutputTokens = 0;

    public MessageStreamer(IMessageRepository messageRepository, TokenUsageService tokenUsageService,
        IMediator mediator, StreamingOperationManager streamingOperationManager)
    {
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _tokenUsageService = tokenUsageService ?? throw new ArgumentNullException(nameof(tokenUsageService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _streamingOperationManager = streamingOperationManager ??
                                     throw new ArgumentNullException(nameof(streamingOperationManager));
    }

    public async Task StreamResponseAsync(ChatSession chatSession, Message aiMessage, IAiModelService aiService,
        List<MessageDto> messages, CancellationToken cancellationToken = default)
    {
        var cts = new CancellationTokenSource();
        _streamingOperationManager.RegisterOperation(aiMessage.Id, cts);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

        // Deduplicate messages using MessageId
        var distinctMessages = messages
            .DistinctBy(m => m.MessageId)
            .ToList();

        var tokenUsage = await _tokenUsageService.GetOrCreateTokenUsageAsync(chatSession.Id, cancellationToken);
        var responseContent = new StringBuilder();
        bool shouldEnableThinking = chatSession.EnableThinking && chatSession.AiModel.SupportsThinking;

        // Reset token accumulators for this message
        _totalInputTokens = 0;
        _totalOutputTokens = 0;
        _inputTokensAdded = false;
        _lastOutputTokens = 0;

        try
        {
            await foreach (var response in aiService.StreamResponseAsync(distinctMessages, linkedCts.Token))
            {
                if (linkedCts.Token.IsCancellationRequested)
                {
                    await HandleCancellation(chatSession, aiMessage);
                    break;
                }

                if (shouldEnableThinking && response.IsThinking)
                {
                    await ProcessThinkingChunkAsync(chatSession, aiMessage, response, tokenUsage, linkedCts.Token);
                }
                else
                {
                    await ProcessChunkAsync(chatSession, aiMessage, response, responseContent, tokenUsage,
                        linkedCts.Token);
                }
            }

            if (!linkedCts.Token.IsCancellationRequested)
            {
                await FinalizeMessageAsync(aiMessage, tokenUsage, chatSession, cancellationToken);
                await _mediator.Publish(new ResponseCompletedNotification(chatSession.Id, aiMessage.Id),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            await HandleCancellation(chatSession, aiMessage);
        }
        finally
        {
            _streamingOperationManager.StopStreaming(aiMessage.Id);
            cts.Dispose();
        }
    }

    private async Task HandleCancellation(ChatSession chatSession, Message aiMessage)
    {
        aiMessage.AppendContent("\n[Response interrupted]");
        aiMessage.InterruptMessage();
        await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
        await _mediator.Publish(
            new MessageChunkReceivedNotification(chatSession.Id, aiMessage.Id, "[Response interrupted]"),
            CancellationToken.None);
        await _mediator.Publish(
            new ResponseStoppedNotification(chatSession.Id, aiMessage.Id),
            CancellationToken.None);
    }

    private async Task ProcessThinkingChunkAsync(ChatSession chatSession, Message aiMessage, StreamResponse response,
        ChatTokenUsage tokenUsage, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var chunk = response.Content;
        var currentInputTokens = response.InputTokens;
        var currentOutputTokens = response.OutputTokens;

        // Accumulate input tokens only once
        if (!_inputTokensAdded && currentInputTokens > 0)
        {
            _totalInputTokens += currentInputTokens;
            _inputTokensAdded = true;
        }

        // Accumulate output tokens
        int outputTokenDelta = currentOutputTokens - _lastOutputTokens;
        if (outputTokenDelta > 0)
        {
            _totalOutputTokens += outputTokenDelta;
            _lastOutputTokens = currentOutputTokens;
        }

        await _mediator.Publish(
            new ThinkingChunkReceivedNotification(chatSession.Id, aiMessage.Id, chunk),
            cancellationToken);
    }

    private async Task ProcessChunkAsync(ChatSession chatSession, Message aiMessage, StreamResponse response,
        StringBuilder responseContent, ChatTokenUsage tokenUsage, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var chunk = response.Content;
        var currentInputTokens = response.InputTokens;
        var currentOutputTokens = response.OutputTokens;

        // Accumulate input tokens only once
        if (!_inputTokensAdded && currentInputTokens > 0)
        {
            _totalInputTokens += currentInputTokens;
            _inputTokensAdded = true;
        }

        // Accumulate output tokens
        int outputTokenDelta = currentOutputTokens - _lastOutputTokens;
        if (outputTokenDelta > 0)
        {
            _totalOutputTokens += outputTokenDelta;
            _lastOutputTokens = currentOutputTokens;
        }

        responseContent.Append(chunk);
        aiMessage.AppendContent(chunk);
        await _messageRepository.UpdateAsync(aiMessage, cancellationToken);
        await _mediator.Publish(new MessageChunkReceivedNotification(chatSession.Id, aiMessage.Id, chunk),
            cancellationToken);
    }

    private async Task FinalizeMessageAsync(Message aiMessage, ChatTokenUsage tokenUsage, ChatSession chatSession,
        CancellationToken cancellationToken)
    {
        aiMessage.CompleteMessage();
        await _messageRepository.UpdateAsync(aiMessage, cancellationToken);

        // Calculate total cost based on accumulated tokens
        decimal inputCost = chatSession.AiModel.CalculateCost(_totalInputTokens, 0);
        decimal outputCost = chatSession.AiModel.CalculateCost(0, _totalOutputTokens);
        decimal totalCost = inputCost + outputCost;

        // Update token usage in the database once
        await _tokenUsageService.UpdateTokenUsageAsync(
            chatSession.Id,
            _totalInputTokens,
            _totalOutputTokens,
            totalCost,
            cancellationToken);

        // Reset accumulators
        _totalInputTokens = 0;
        _totalOutputTokens = 0;
        _inputTokensAdded = false;
        _lastOutputTokens = 0;
    }
}