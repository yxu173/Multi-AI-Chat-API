using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Domain.Repositories;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.Messaging;

internal class AiMessageFinalizer : IAiMessageFinalizer
{
    private readonly IMessageRepository _messageRepository;
    private readonly ILogger<AiMessageFinalizer> _logger;

    public AiMessageFinalizer(IMessageRepository messageRepository, ILogger<AiMessageFinalizer> logger)
    {
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task FinalizeProgressingMessageAsync(Message aiMessage, bool aiResponseCompletedSuccessfully, CancellationToken persistenceToken)
    {
        if (aiMessage.Status == MessageStatus.Streaming)
        {
            if (aiResponseCompletedSuccessfully)
            {
                aiMessage.CompleteMessage();
            }
            else
            {
                aiMessage.InterruptMessage(); 
            }
        }

        await _messageRepository.UpdateAsync(aiMessage, persistenceToken);
        _logger.LogInformation("Saved final state for message {MessageId} with status {Status} after processing.", aiMessage.Id, aiMessage.Status);

        if (aiMessage.Status == MessageStatus.Completed)
        {
            await new ResponseCompletedNotification(aiMessage.ChatSessionId, aiMessage.Id).PublishAsync(cancellation: persistenceToken);
        }
        else if (aiMessage.Status == MessageStatus.Interrupted)
        {
            await new ResponseStoppedNotification(aiMessage.ChatSessionId, aiMessage.Id).PublishAsync(cancellation: persistenceToken);
        }
    }

    public async Task FinalizeAfterCancellationAsync(Message aiMessage, bool wasDirectUserCancellation, CancellationToken persistenceToken)
    {
        var cancellationReason = wasDirectUserCancellation ? "User Request" : "Internal Stop Command";
        _logger.LogInformation("Streaming operation for message {MessageId} cancelled. Reason: {Reason}", aiMessage.Id, cancellationReason);

        if (!aiMessage.IsTerminal())
        {
            aiMessage.AppendContent($"\n[{cancellationReason}]");
            aiMessage.InterruptMessage(); 
            await _messageRepository.UpdateAsync(aiMessage, persistenceToken);
            _logger.LogInformation("Saved final state for message {MessageId} as Interrupted due to cancellation.", aiMessage.Id);
        }
        await new ResponseStoppedNotification(aiMessage.ChatSessionId, aiMessage.Id).PublishAsync(cancellation: CancellationToken.None);
    }

    public async Task FinalizeAfterErrorAsync(Message aiMessage, Exception ex, CancellationToken persistenceToken)
    {
        _logger.LogError(ex, "Error during AI response streaming for message {MessageId}.", aiMessage.Id);
        if (!aiMessage.IsTerminal())
        {
            aiMessage.AppendContent($"\n[Error: {ex.Message}]");
            aiMessage.FailMessage();
            await _messageRepository.UpdateAsync(aiMessage, persistenceToken);
            _logger.LogInformation("Saved final state for message {MessageId} as Failed due to error.", aiMessage.Id);
        }
        await new ResponseStoppedNotification(aiMessage.ChatSessionId, aiMessage.Id).PublishAsync(cancellation: CancellationToken.None);
    }
}

public interface IAiMessageFinalizer
{
    Task FinalizeProgressingMessageAsync(Message aiMessage, bool aiResponseCompletedSuccessfully, CancellationToken persistenceToken);
    Task FinalizeAfterCancellationAsync(Message aiMessage, bool wasDirectUserCancellation, CancellationToken persistenceToken);
    Task FinalizeAfterErrorAsync(Message aiMessage, Exception ex, CancellationToken persistenceToken);
}
