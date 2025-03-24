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

        var tokenUsage = await _tokenUsageService.GetOrCreateTokenUsageAsync(chatSession.Id, cancellationToken);
        var previousInputTokens = tokenUsage.InputTokens;
        var previousOutputTokens = tokenUsage.OutputTokens;
        var previousCost = tokenUsage.TotalCost;
        var responseContent = new StringBuilder();
        
        // Check if thinking should be enabled for this chat
        bool shouldEnableThinking = chatSession.EnableThinking && chatSession.AiModel.SupportsThinking;

        try
        {
            await foreach (var response in aiService.StreamResponseAsync(messages, linkedCts.Token))
            {
                if (linkedCts.Token.IsCancellationRequested)
                {
                    await HandleCancellation(chatSession, aiMessage);
                    break;
                }

                // Handle thinking responses differently if thinking is enabled
                if (shouldEnableThinking && response.IsThinking)
                {
                    await ProcessThinkingChunkAsync(chatSession, aiMessage, response, tokenUsage,
                        previousInputTokens, previousOutputTokens, previousCost, linkedCts.Token);
                }
                else
                {
                    await ProcessChunkAsync(chatSession, aiMessage, response, responseContent, tokenUsage,
                        previousInputTokens, previousOutputTokens, previousCost, linkedCts.Token);
                }
            }

            if (!linkedCts.Token.IsCancellationRequested)
            {
                await FinalizeMessageAsync(aiMessage, tokenUsage, chatSession, previousInputTokens,
                    previousOutputTokens, previousCost, cancellationToken);
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
        ChatTokenUsage tokenUsage, int previousInputTokens, int previousOutputTokens,
        decimal previousCost, CancellationToken cancellationToken)
    {
        // Early exit if cancellation is requested
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var chunk = response.Content;
        var currentInputTokens = response.InputTokens;
        var currentOutputTokens = response.OutputTokens;

        tokenUsage.UpdateTokenCounts(currentInputTokens, currentOutputTokens);
        var cost = chatSession.AiModel.CalculateCost(currentInputTokens, currentOutputTokens);
        await _tokenUsageService.UpdateTokenUsageAsync(chatSession.Id, currentInputTokens, currentOutputTokens, cost,
            cancellationToken);
        
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // We don't append thinking output to the actual message content
        // Instead we send it as a special notification type for the frontend to display differently
        await _mediator.Publish(
            new ThinkingChunkReceivedNotification(chatSession.Id, aiMessage.Id, chunk),
            cancellationToken);
    }

    private async Task ProcessChunkAsync(ChatSession chatSession, Message aiMessage, StreamResponse response,
        StringBuilder responseContent, ChatTokenUsage tokenUsage, int previousInputTokens, int previousOutputTokens,
        decimal previousCost, CancellationToken cancellationToken)
    {
        // Early exit if cancellation is requested
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var chunk = response.Content;
        var currentInputTokens = response.InputTokens;
        var currentOutputTokens = response.OutputTokens;

        tokenUsage.UpdateTokenCounts(currentInputTokens, currentOutputTokens);
        var cost = chatSession.AiModel.CalculateCost(currentInputTokens, currentOutputTokens);
        await _tokenUsageService.UpdateTokenUsageAsync(chatSession.Id, currentInputTokens, currentOutputTokens, cost,
            cancellationToken);
        
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        responseContent.Append(chunk);
        aiMessage.AppendContent(chunk);
        await _messageRepository.UpdateAsync(aiMessage, cancellationToken);
        await _mediator.Publish(new MessageChunkReceivedNotification(chatSession.Id, aiMessage.Id, chunk),
            cancellationToken);
    }

    private async Task FinalizeMessageAsync(Message aiMessage, ChatTokenUsage tokenUsage, ChatSession chatSession,
        int previousInputTokens, int previousOutputTokens, decimal previousCost, CancellationToken cancellationToken)
    {
        aiMessage.CompleteMessage();
        await _messageRepository.UpdateAsync(aiMessage, cancellationToken);

        var finalCost = chatSession.AiModel.CalculateCost(tokenUsage.InputTokens - previousInputTokens,
            tokenUsage.OutputTokens - previousOutputTokens);
        await _tokenUsageService.UpdateTokenUsageAsync(chatSession.Id, tokenUsage.InputTokens - previousInputTokens,
            tokenUsage.OutputTokens - previousOutputTokens, finalCost, cancellationToken);
    }
}