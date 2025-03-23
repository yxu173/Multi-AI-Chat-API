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
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        bool wasCanceled = false;

        try
        {
            var tokenUsage = await _tokenUsageService.GetOrCreateTokenUsageAsync(chatSession.Id, linkedCts.Token);
            var previousInputTokens = tokenUsage.InputTokens;
            var previousOutputTokens = tokenUsage.OutputTokens;
            var previousCost = tokenUsage.TotalCost;
            var responseContent = new StringBuilder();

            try
            {
                await foreach (var response in aiService.StreamResponseAsync(messages, linkedCts.Token))
                {
                    await ProcessChunkAsync(chatSession, aiMessage, response, responseContent, tokenUsage,
                        previousInputTokens, previousOutputTokens, previousCost, linkedCts.Token);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                aiMessage.AppendContent("\n[Error occurred during response]");
                aiMessage.InterruptMessage();
                await _messageRepository.UpdateAsync(aiMessage, cancellationToken);
                await _mediator.Publish(
                    new MessageChunkReceivedNotification(chatSession.Id, aiMessage.Id,
                        "[Error occurred during response]"), cancellationToken);
            }

            await FinalizeMessageAsync(aiMessage, tokenUsage, chatSession, previousInputTokens, previousOutputTokens,
                previousCost, linkedCts.Token);
        }
        finally
        {
            if (linkedCts.IsCancellationRequested) wasCanceled = true;
            linkedCts.Dispose();
            if (wasCanceled)
            {
                aiMessage.AppendContent("\n[Response interrupted]");
                aiMessage.InterruptMessage();
                await _messageRepository.UpdateAsync(aiMessage, cancellationToken);
                await _mediator.Publish(
                    new MessageChunkReceivedNotification(chatSession.Id, aiMessage.Id, "[Response interrupted]"),
                    cancellationToken);
            }

            _streamingOperationManager.StopStreaming(aiMessage.Id);
            await _mediator.Publish(new ResponseCompletedNotification(chatSession.Id, aiMessage.Id), cancellationToken);
        }
    }

    private async Task ProcessChunkAsync(ChatSession chatSession, Message aiMessage, StreamResponse response,
        StringBuilder responseContent, ChatTokenUsage tokenUsage, int previousInputTokens, int previousOutputTokens,
        decimal previousCost, CancellationToken cancellationToken)
    {
        var chunk = response.Content;
        var currentInputTokens = response.InputTokens;
        var currentOutputTokens = response.OutputTokens;

        tokenUsage.UpdateTokenCounts(currentInputTokens, currentOutputTokens);
        var cost = chatSession.AiModel.CalculateCost(currentInputTokens, currentOutputTokens);
        await _tokenUsageService.UpdateTokenUsageAsync(chatSession.Id, currentInputTokens, currentOutputTokens, cost,
            cancellationToken);

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